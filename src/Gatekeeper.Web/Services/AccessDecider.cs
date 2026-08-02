using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>Answers the question Gatekeeper exists to answer: may this subject take this action on this resource?</summary>
public interface IAccessDecider
{
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);
}

/// <summary>
/// Evaluates the decision policy for a single (subject, action, resource) request.
///
/// Resolution:
///   1. The set of principals a grant may apply through is the subject itself plus every
///      role assigned to it and, transitively, every ancestor of those roles.
///   2. A grant applies when it hangs off one of those principals, names the same action,
///      and its resource is the requested resource (or "*").
///   3. An explicit Deny always wins over an Allow. With no matching grant at all, the
///      answer is Deny — access is denied by default.
/// </summary>
public class AccessDecider(IDbContextFactory<GatekeeperDbContext> dbFactory) : IAccessDecider
{
    // A safety bound on inheritance depth so a malformed (cyclic) role graph cannot loop forever.
    private const int MaxRoleDepth = 1000;

    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Roles assigned directly to the subject.
        var directRoles = await db.PolicyAssignments
            .Where(a => a.Subject == subject)
            .Select(a => a.RoleName)
            .ToListAsync(ct);

        // Name -> parent-name map used to walk the inheritance chain.
        var parentOf = await db.PolicyRoles
            .ToDictionaryAsync(r => r.Name, r => r.ParentName, ct);

        // Everything a grant may be attached to for this subject: the subject id plus the
        // closure of its roles over parent inheritance.
        var principals = new HashSet<string>(StringComparer.Ordinal) { subject };
        foreach (var role in directRoles)
        {
            AddRoleAndAncestors(role, parentOf, principals);
        }

        // Every grant that could bear on this request.
        var effects = await db.PolicyGrants
            .Where(g => g.Action == action
                        && (g.Resource == resource || g.Resource == "*")
                        && principals.Contains(g.Subject))
            .Select(g => g.Effect)
            .ToListAsync(ct);

        // Explicit Deny overrides Allow; absence of any grant denies by default.
        if (effects.Contains(GrantEffect.Deny))
        {
            return GrantEffect.Deny;
        }
        if (effects.Contains(GrantEffect.Allow))
        {
            return GrantEffect.Allow;
        }
        return GrantEffect.Deny;
    }

    private static void AddRoleAndAncestors(string role, IReadOnlyDictionary<string, string?> parentOf, HashSet<string> principals)
    {
        var current = role;
        var depth = 0;

        // principals.Add returns false once a role has already been seen, which both
        // deduplicates and breaks any cycle in the role graph.
        while (current is not null && principals.Add(current) && depth++ < MaxRoleDepth)
        {
            current = parentOf.TryGetValue(current, out var parent) ? parent : null;
        }
    }
}
