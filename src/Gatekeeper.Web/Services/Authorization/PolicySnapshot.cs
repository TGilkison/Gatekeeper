using Gatekeeper.Web.Data;

namespace Gatekeeper.Web.Services.Authorization;

/// <summary>
/// An in-memory, read-only view of the whole policy that the <see cref="PermissionEvaluator"/>
/// decides against. Built once per decision from the stored policy.
/// </summary>
public sealed class PolicySnapshot
{
    private readonly Dictionary<string, string?> _parentByRole;
    private readonly ILookup<string, string> _rolesBySubject;

    public PolicySnapshot(
        IEnumerable<PolicyRole> roles,
        IEnumerable<PolicyAssignment> assignments,
        IReadOnlyList<PolicyGrant> grants)
    {
        // Last writer wins if a role name somehow appears twice; the unique index normally
        // prevents that, but the evaluator must never throw on its inputs.
        _parentByRole = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            _parentByRole[role.Name] = role.ParentName;
        }

        _rolesBySubject = assignments.ToLookup(a => a.Subject, a => a.RoleName, StringComparer.Ordinal);
        Grants = grants;
    }

    public IReadOnlyList<PolicyGrant> Grants { get; }

    /// <summary>Roles assigned directly to a subject (empty if none).</summary>
    public IEnumerable<string> RolesFor(string subject) => _rolesBySubject[subject];

    /// <summary>The parent of a role, or null if it has none or the role is unknown.</summary>
    public string? ParentOf(string role) =>
        _parentByRole.TryGetValue(role, out var parent) ? parent : null;
}
