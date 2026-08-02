using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services.Authorization;

/// <summary>
/// The authorization service the HTTP API is built on: it answers permission questions,
/// records every answer to the decision audit log, and lets the whole policy be replaced.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Decides whether <paramref name="subject"/> may perform <paramref name="action"/> on
    /// <paramref name="resource"/>, and records the decision in the audit log.
    /// </summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Replaces the entire stored policy (roles, assignments, grants) in one transaction.</summary>
    Task ReplacePolicyAsync(
        IReadOnlyList<PolicyRole> roles,
        IReadOnlyList<PolicyAssignment> assignments,
        IReadOnlyList<PolicyGrant> grants,
        CancellationToken ct = default);

    /// <summary>Decision-log entries for a subject/resource pair, oldest first.</summary>
    Task<IReadOnlyList<DecisionAuditEntry>> GetAuditAsync(string subject, string resource, CancellationToken ct = default);
}

public sealed class PermissionService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPermissionService
{
    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // The policy is small (a single tenant's rules); load it and decide in memory.
        var snapshot = new PolicySnapshot(
            await db.PolicyRoles.AsNoTracking().ToListAsync(ct),
            await db.PolicyAssignments.AsNoTracking().ToListAsync(ct),
            await db.PolicyGrants.AsNoTracking().ToListAsync(ct));

        var outcome = PermissionEvaluator.Decide(snapshot, subject, action, resource);

        // Record the decision exactly as returned to the caller: an audit log that
        // disagrees with the answer it describes is worse than no audit log at all.
        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = outcome,
        });
        await db.SaveChangesAsync(ct);

        return outcome;
    }

    public async Task ReplacePolicyAsync(
        IReadOnlyList<PolicyRole> roles,
        IReadOnlyList<PolicyAssignment> assignments,
        IReadOnlyList<PolicyGrant> grants,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // "Replace the whole policy in one shot": wipe the old rules, then insert the new.
        // The decision audit log is deliberately left untouched — it is a permanent record.
        await db.PolicyGrants.ExecuteDeleteAsync(ct);
        await db.PolicyAssignments.ExecuteDeleteAsync(ct);
        await db.PolicyRoles.ExecuteDeleteAsync(ct);

        db.PolicyRoles.AddRange(roles);
        db.PolicyAssignments.AddRange(assignments);
        db.PolicyGrants.AddRange(grants);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<DecisionAuditEntry>> GetAuditAsync(string subject, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DecisionAudit
            .AsNoTracking()
            .Where(e => e.Subject == subject && e.Resource == resource)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);
    }
}
