using System.Globalization;
using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Authorization;

/// <summary>Loads the stored policy, decides authorization requests, records them, and serves the audit log.</summary>
public interface IPolicyService
{
    /// <summary>Decide a request, write it to the decision audit log, and return the effect.</summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Replace the entire stored policy with <paramref name="document"/>.</summary>
    Task ReplacePolicyAsync(PolicyDocument document, CancellationToken ct = default);

    /// <summary>Return decision audit entries, oldest first, filtered by subject and/or resource.</summary>
    Task<IReadOnlyList<AuditEntry>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default);
}

public class PolicyService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPolicyService
{
    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // The policy tables are small; read them straight and let the pure evaluator decide.
        var roles = await db.PolicyRoles.AsNoTracking().ToListAsync(ct);
        var assignments = await db.RoleAssignments.AsNoTracking().ToListAsync(ct);
        var grants = await db.PolicyGrants.AsNoTracking().ToListAsync(ct);

        var outcome = PolicyEvaluator.Decide(subject, action, resource, roles, assignments, grants);

        // Record the effect actually returned to the caller. This runs for every decision, allow or deny.
        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = outcome,
            Timestamp = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return outcome;
    }

    public async Task ReplacePolicyAsync(PolicyDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Translate the wire document into entities up front so a bad effect string fails the whole
        // request before we touch the stored policy.
        var roles = (document.Roles ?? [])
            .Select(r => new PolicyRole { Name = Require(r.Name, "role name"), ParentName = NullIfBlank(r.Parent) })
            .ToList();

        var assignments = (document.Assignments ?? [])
            .Select(a => new RoleAssignment { Subject = Require(a.Subject, "assignment subject"), RoleName = Require(a.Role, "assignment role") })
            .ToList();

        var grants = (document.Grants ?? [])
            .Select(g => new PolicyGrant
            {
                Subject = Require(g.Subject, "grant subject"),
                Action = Require(g.Action, "grant action"),
                Resource = NullIfBlank(g.Resource) ?? "*",
                Effect = ParseEffect(g.Effect),
            })
            .ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // "Replace the whole policy in one shot": clear the three policy tables, then insert the new
        // set. The decision audit log is history and is deliberately left untouched.
        await db.PolicyGrants.ExecuteDeleteAsync(ct);
        await db.RoleAssignments.ExecuteDeleteAsync(ct);
        await db.PolicyRoles.ExecuteDeleteAsync(ct);

        db.PolicyRoles.AddRange(roles);
        db.RoleAssignments.AddRange(assignments);
        db.PolicyGrants.AddRange(grants);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        IQueryable<DecisionAuditEntry> query = db.DecisionAudit.AsNoTracking();
        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(e => e.Subject == subject);
        }
        if (!string.IsNullOrEmpty(resource))
        {
            query = query.Where(e => e.Resource == resource);
        }

        var rows = await query
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        return rows
            .Select(e => new AuditEntry(e.Subject, e.Action, e.Resource, e.Outcome.ToString(), FormatTimestamp(e.Timestamp)))
            .ToList();
    }

    /// <summary>Format as UTC ISO-8601 with a trailing Z and no fractional seconds, e.g. 2026-07-12T18:03:11Z.</summary>
    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static GrantEffect ParseEffect(string? effect)
    {
        if (Enum.TryParse<GrantEffect>(effect, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Effect must be \"Allow\" or \"Deny\", got \"{effect}\".");
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"Missing {field}.") : value;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
