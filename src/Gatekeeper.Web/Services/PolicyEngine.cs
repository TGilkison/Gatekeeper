using Gatekeeper.Web.Api;
using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>
/// Answers the question Gatekeeper exists to answer: may a subject take an action on a
/// resource? Also owns bulk replacement of the policy and reading the decision log.
/// </summary>
public interface IPolicyEngine
{
    /// <summary>
    /// Decides whether <paramref name="subject"/> may perform <paramref name="action"/> on
    /// <paramref name="resource"/>, and records the decision in the audit log. The returned
    /// effect and the logged outcome are always the same value.
    /// </summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Replaces the entire policy (roles, assignments, grants) in one atomic operation.</summary>
    Task ReplacePolicyAsync(PolicyDocument policy, CancellationToken ct = default);

    /// <summary>Reads decision-log entries, optionally filtered by subject and/or resource, oldest first.</summary>
    Task<IReadOnlyList<DecisionAuditEntry>> ReadAuditAsync(string? subject, string? resource, CancellationToken ct = default);
}

public class PolicyEngine(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPolicyEngine
{
    /// <summary>
    /// The API-managed policy lives under its own tenant so a full policy replace never
    /// touches the demo data the console seeds, and role names stay unique per tenant.
    /// </summary>
    public const string TenantName = "API Clients";

    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var effect = await EvaluateAsync(db, subject, action, resource, ct);

        // Record the decision that was actually reached — Allow logged as Allow, Deny as Deny.
        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = effect,
        });
        await db.SaveChangesAsync(ct);

        return effect;
    }

    private static async Task<GrantEffect> EvaluateAsync(
        GatekeeperDbContext db, string subject, string action, string resource, CancellationToken ct)
    {
        var tenantId = await db.Customers
            .Where(c => c.Name == TenantName)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // No policy has been loaded yet: deny by default.
        if (tenantId is null)
        {
            return GrantEffect.Deny;
        }

        var roleIds = await EffectiveRoleIdsAsync(db, tenantId.Value, subject, ct);

        // Every grant that could speak to this request: those hanging off the subject directly,
        // plus those on any role the subject holds (directly or by inheritance). A grant applies
        // when its permission and resource match, with "*" acting as a wildcard for either.
        var applicable = await db.Grants
            .Where(g => g.CustomerId == tenantId.Value)
            .Where(g => g.UserId == subject || (g.RoleId != null && roleIds.Contains(g.RoleId.Value)))
            .Where(g => g.Permission == action || g.Permission == "*")
            .Where(g => g.Resource == resource || g.Resource == "*")
            .Select(g => g.Effect)
            .ToListAsync(ct);

        // Precedence: an explicit Deny always wins over an Allow, and the absence of any
        // matching grant is itself a Deny. This is deliberately deny-by-default.
        if (applicable.Contains(GrantEffect.Deny))
        {
            return GrantEffect.Deny;
        }
        if (applicable.Contains(GrantEffect.Allow))
        {
            return GrantEffect.Allow;
        }
        return GrantEffect.Deny;
    }

    /// <summary>
    /// The set of role ids the subject effectively holds: every directly assigned role and
    /// all of its ancestors via the parent chain. A <see cref="HashSet{T}"/> of visited ids
    /// makes the walk safe even if the role graph contains a cycle.
    /// </summary>
    private static async Task<HashSet<Guid>> EffectiveRoleIdsAsync(
        GatekeeperDbContext db, Guid tenantId, string subject, CancellationToken ct)
    {
        var assigned = await db.UserRoles
            .Where(ur => ur.UserId == subject)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var effective = new HashSet<Guid>();
        if (assigned.Count == 0)
        {
            return effective;
        }

        // Load the tenant's role graph once and walk parents in memory.
        var parentOf = await db.Roles
            .Where(r => r.CustomerId == tenantId)
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToDictionaryAsync(r => r.Id, r => r.ParentRoleId, ct);

        foreach (var start in assigned)
        {
            Guid? current = start;
            while (current is not null && effective.Add(current.Value))
            {
                current = parentOf.TryGetValue(current.Value, out var parent) ? parent : null;
            }
        }

        return effective;
    }

    public async Task ReplacePolicyAsync(PolicyDocument policy, CancellationToken ct = default)
    {
        var roles = policy.Roles ?? Array.Empty<PolicyRole>();
        var assignments = policy.Assignments ?? Array.Empty<PolicyAssignment>();
        var grants = policy.Grants ?? Array.Empty<PolicyGrant>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var tenant = await db.Customers.FirstOrDefaultAsync(c => c.Name == TenantName, ct);
        if (tenant is null)
        {
            tenant = new Customer { Name = TenantName, PlanTier = PlanTier.Enterprise };
            db.Customers.Add(tenant);
            await db.SaveChangesAsync(ct);
        }
        var tenantId = tenant.Id;

        // Wipe the previous policy for this tenant. Order matters: dependents before principals.
        var oldUserIds = await db.Users
            .Where(u => u.CustomerId == tenantId)
            .Select(u => u.Id)
            .ToListAsync(ct);
        await db.Grants.Where(g => g.CustomerId == tenantId).ExecuteDeleteAsync(ct);
        await db.UserRoles.Where(ur => oldUserIds.Contains(ur.UserId)).ExecuteDeleteAsync(ct);
        await db.Roles.Where(r => r.CustomerId == tenantId).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.CustomerId == tenantId).ExecuteDeleteAsync(ct);

        // Roles first, so grants and assignments can reference them.
        var roleByName = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var r in roles)
        {
            if (string.IsNullOrWhiteSpace(r.Name) || roleByName.ContainsKey(r.Name))
            {
                continue;
            }
            var role = new Role { CustomerId = tenantId, Name = r.Name };
            roleByName[r.Name] = role;
            db.Roles.Add(role);
        }

        // Link parents by name now that every role exists.
        foreach (var r in roles)
        {
            if (r.Parent is not null
                && roleByName.TryGetValue(r.Name, out var child)
                && roleByName.TryGetValue(r.Parent, out var parent))
            {
                child.Parent = parent;
            }
        }

        // A grant's subject is a role name if it matches one, otherwise a user id. Every user
        // id that appears (as a grant subject or an assignment subject) needs a user row to
        // satisfy the foreign keys the grants and assignments hang off.
        var userIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in assignments)
        {
            if (!string.IsNullOrWhiteSpace(a.Subject))
            {
                userIds.Add(a.Subject);
            }
        }
        foreach (var g in grants)
        {
            if (!string.IsNullOrWhiteSpace(g.Subject) && !roleByName.ContainsKey(g.Subject))
            {
                userIds.Add(g.Subject);
            }
        }
        foreach (var id in userIds)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = id,
                DisplayName = id,
                CustomerId = tenantId,
            });
        }

        // Assignments (user -> role).
        foreach (var a in assignments)
        {
            if (!string.IsNullOrWhiteSpace(a.Subject) && roleByName.TryGetValue(a.Role, out var role))
            {
                db.UserRoles.Add(new UserRole { UserId = a.Subject, Role = role });
            }
        }

        // Grants, attached to a role or a user depending on the subject.
        foreach (var g in grants)
        {
            if (string.IsNullOrWhiteSpace(g.Subject))
            {
                continue;
            }
            var effect = ParseEffect(g.Effect);
            var permission = g.Action ?? string.Empty;
            var resource = string.IsNullOrWhiteSpace(g.Resource) ? "*" : g.Resource;

            if (roleByName.TryGetValue(g.Subject, out var role))
            {
                db.Grants.Add(new Grant
                {
                    CustomerId = tenantId,
                    Role = role,
                    Effect = effect,
                    Permission = permission,
                    Resource = resource,
                });
            }
            else
            {
                db.Grants.Add(new Grant
                {
                    CustomerId = tenantId,
                    UserId = g.Subject,
                    Effect = effect,
                    Permission = permission,
                    Resource = resource,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<DecisionAuditEntry>> ReadAuditAsync(
        string? subject, string? resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.DecisionAudit.AsQueryable();
        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(e => e.Subject == subject);
        }
        if (!string.IsNullOrEmpty(resource))
        {
            query = query.Where(e => e.Resource == resource);
        }

        return await query
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
    }

    private static GrantEffect ParseEffect(string? effect) =>
        Enum.TryParse<GrantEffect>(effect, ignoreCase: true, out var parsed)
            ? parsed
            : GrantEffect.Deny; // Fail safe: an unrecognized effect denies.
}
