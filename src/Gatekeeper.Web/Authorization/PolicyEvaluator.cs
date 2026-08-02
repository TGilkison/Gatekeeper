using Gatekeeper.Web.Data;

namespace Gatekeeper.Web.Authorization;

/// <summary>
/// Pure, side-effect-free evaluation of an authorization policy. Given a subject, an action and a
/// resource, it decides <see cref="GrantEffect.Allow"/> or <see cref="GrantEffect.Deny"/> against a
/// snapshot of the policy. No I/O, no logging: callers are responsible for loading the policy and
/// recording the result, which keeps the decision rules unit-testable in isolation.
/// </summary>
public static class PolicyEvaluator
{
    private const string Wildcard = "*";

    /// <summary>
    /// Decide whether <paramref name="subject"/> may take <paramref name="action"/> on
    /// <paramref name="resource"/>.
    /// </summary>
    /// <remarks>
    /// The subject's applicable grants are those attached directly to its user id plus those attached
    /// to any role it holds, including roles inherited through the parent chain. Among the grants that
    /// match the action and resource, precedence is:
    /// <list type="number">
    ///   <item>an explicit <see cref="GrantEffect.Deny"/> wins over any allow;</item>
    ///   <item>otherwise an explicit <see cref="GrantEffect.Allow"/> permits the action;</item>
    ///   <item>otherwise, with nothing matching, the default is to deny.</item>
    /// </list>
    /// Deny-overrides with a closed-world default is the safe rule for an authorization service: a
    /// missing or ambiguous policy denies rather than leaks access.
    /// </remarks>
    public static GrantEffect Decide(
        string subject,
        string action,
        string resource,
        IEnumerable<PolicyRole> roles,
        IEnumerable<RoleAssignment> assignments,
        IEnumerable<PolicyGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource);

        // The set of subject strings whose grants apply: the user itself, plus every role it holds
        // directly or by inheritance. Role names are matched case-sensitively, as stored.
        var effectiveSubjects = new HashSet<string>(StringComparer.Ordinal) { subject };
        foreach (var roleName in EffectiveRoles(subject, roles, assignments))
        {
            effectiveSubjects.Add(roleName);
        }

        var sawAllow = false;
        foreach (var grant in grants)
        {
            if (!effectiveSubjects.Contains(grant.Subject))
            {
                continue;
            }

            if (!Matches(grant.Action, action) || !Matches(grant.Resource, resource))
            {
                continue;
            }

            // Deny short-circuits: no later allow can override an explicit deny.
            if (grant.Effect == GrantEffect.Deny)
            {
                return GrantEffect.Deny;
            }

            sawAllow = true;
        }

        return sawAllow ? GrantEffect.Allow : GrantEffect.Deny;
    }

    /// <summary>
    /// The names of every role the subject holds: those assigned directly plus all ancestors reached
    /// by walking parent links. A <c>visited</c> set makes the walk safe against cycles and shared
    /// ancestors, so a malformed role graph cannot loop forever.
    /// </summary>
    private static HashSet<string> EffectiveRoles(
        string subject,
        IEnumerable<PolicyRole> roles,
        IEnumerable<RoleAssignment> assignments)
    {
        var parentByName = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            // Last definition wins if a name is repeated; the store keeps names unique anyway.
            parentByName[role.Name] = role.ParentName;
        }

        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            if (!string.Equals(assignment.Subject, subject, StringComparison.Ordinal))
            {
                continue;
            }

            // Walk this role's ancestor chain, stopping on a cycle, a missing parent, or a role we
            // have already expanded via another assignment.
            var current = assignment.RoleName;
            while (current is not null && held.Add(current))
            {
                current = parentByName.TryGetValue(current, out var parent) ? parent : null;
            }
        }

        return held;
    }

    /// <summary>A grant pattern matches a request value on an exact string or the "*" wildcard.</summary>
    private static bool Matches(string pattern, string value) =>
        pattern == Wildcard || string.Equals(pattern, value, StringComparison.Ordinal);
}
