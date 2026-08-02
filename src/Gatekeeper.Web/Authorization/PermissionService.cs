using System.Globalization;
using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Authorization;

/// <summary>Thrown when a PUT /api/policy body is malformed. Maps to HTTP 400.</summary>
public sealed class PolicyValidationException(string message) : Exception(message);

/// <summary>
/// Answers the question Gatekeeper exists to answer: may this subject take this
/// action on this resource? Also owns the decision audit log and whole-policy
/// replacement.
/// </summary>
public interface IPermissionService
{
    /// <summary>Decides the effect for a subject/action/resource and records it in the audit log.</summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Replaces the entire policy (roles, assignments, grants) in one shot.</summary>
    Task ReplacePolicyAsync(PolicyDocument policy, CancellationToken ct = default);

    /// <summary>Returns decision-audit entries, oldest first, optionally filtered by subject and/or resource.</summary>
    Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default);
}

public sealed class PermissionService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPermissionService
{
    /// <summary>A grant whose action/resource is this token matches any action/resource.</summary>
    private const string Wildcard = "*";

    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // The subjects whose grants can apply to this decision: the user itself,
        // plus every role it holds transitively through the parent chain.
        var subjects = await ResolveSubjectClosureAsync(db, subject, ct);

        // Only grants that match the action and resource are in play. A grant with a
        // "*" action or resource matches anything.
        var applicable = await db.PolicyGrants
            .Where(g => subjects.Contains(g.Subject)
                && (g.Action == action || g.Action == Wildcard)
                && (g.Resource == resource || g.Resource == Wildcard))
            .ToListAsync(ct);

        // Precedence: default deny, and an explicit Deny always wins over any Allow.
        // Deciding this way means a Deny can never be silently dropped.
        GrantEffect effect;
        if (applicable.Any(g => g.Effect == GrantEffect.Deny))
            effect = GrantEffect.Deny;
        else if (applicable.Any(g => g.Effect == GrantEffect.Allow))
            effect = GrantEffect.Allow;
        else
            effect = GrantEffect.Deny;

        // Record the exact answer we are about to return. `effect` is the single
        // source of truth for both the log and the caller's response.
        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = effect,
            Timestamp = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return effect;
    }

    /// <summary>
    /// Resolves the full set of grant subjects that apply to a user: the user id
    /// itself plus every role reachable through role assignments and parent links.
    /// A visited set makes a cyclic or self-referential role graph terminate.
    /// </summary>
    private static async Task<List<string>> ResolveSubjectClosureAsync(
        GatekeeperDbContext db, string subject, CancellationToken ct)
    {
        // name -> parent name, for every role in the policy.
        var parentByRole = await db.PolicyRoles
            .ToDictionaryAsync(r => r.Name, r => r.ParentName, ct);

        var directRoles = await db.PolicyAssignments
            .Where(a => a.Subject == subject)
            .Select(a => a.RoleName)
            .ToListAsync(ct);

        var roles = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(directRoles);
        while (pending.Count > 0)
        {
            var role = pending.Dequeue();
            if (!roles.Add(role))
                continue; // already visited — stops cycles and diamonds

            if (parentByRole.TryGetValue(role, out var parent) && parent is not null)
                pending.Enqueue(parent);
        }

        // The user subject participates alongside its roles.
        var subjects = new List<string>(roles) { subject };
        return subjects;
    }

    public async Task ReplacePolicyAsync(PolicyDocument policy, CancellationToken ct = default)
    {
        var roles = Normalize(policy.Roles);
        var assignments = Normalize(policy.Assignments);
        var grants = Normalize(policy.Grants);

        // Validate before touching anything, so a bad body never partially applies.
        var newRoles = roles.Select(r => new PolicyRole
        {
            Name = Require(r.Name, "roles[].name"),
            ParentName = string.IsNullOrWhiteSpace(r.Parent) ? null : r.Parent,
        }).ToList();

        var newAssignments = assignments.Select(a => new PolicyAssignment
        {
            Subject = Require(a.Subject, "assignments[].subject"),
            RoleName = Require(a.Role, "assignments[].role"),
        }).ToList();

        var newGrants = grants.Select(g => new PolicyGrant
        {
            Subject = Require(g.Subject, "grants[].subject"),
            Action = Require(g.Action, "grants[].action"),
            Resource = Require(g.Resource, "grants[].resource"),
            Effect = ParseEffect(g.Effect),
        }).ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Whole-policy replacement: clear the previous policy, then insert the new one.
        // The decision audit is history and is deliberately left untouched.
        await db.PolicyGrants.ExecuteDeleteAsync(ct);
        await db.PolicyAssignments.ExecuteDeleteAsync(ct);
        await db.PolicyRoles.ExecuteDeleteAsync(ct);

        db.PolicyRoles.AddRange(newRoles);
        db.PolicyAssignments.AddRange(newAssignments);
        db.PolicyGrants.AddRange(newGrants);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(
        string? subject, string? resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.DecisionAudit.AsQueryable();
        if (!string.IsNullOrEmpty(subject))
            query = query.Where(e => e.Subject == subject);
        if (!string.IsNullOrEmpty(resource))
            query = query.Where(e => e.Resource == resource);

        var rows = await query
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        return rows
            .Select(e => new AuditEntryDto(
                e.Subject,
                e.Action,
                e.Resource,
                e.Outcome.ToString(),
                FormatTimestamp(e.Timestamp)))
            .ToList();
    }

    private static IReadOnlyList<T> Normalize<T>(IReadOnlyList<T>? items) => items ?? Array.Empty<T>();

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new PolicyValidationException($"'{field}' is required.")
            : value;

    private static GrantEffect ParseEffect(string? effect)
    {
        if (Enum.TryParse<GrantEffect>(effect, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new PolicyValidationException($"grants[].effect must be \"Allow\" or \"Deny\", got '{effect}'.");
    }

    /// <summary>Formats as UTC ISO-8601 with a trailing Z and second precision, e.g. 2026-07-12T18:03:11Z.</summary>
    private static string FormatTimestamp(DateTimeOffset ts) =>
        ts.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
