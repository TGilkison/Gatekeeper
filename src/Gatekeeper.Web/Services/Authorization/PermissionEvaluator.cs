using Gatekeeper.Web.Data;

namespace Gatekeeper.Web.Services.Authorization;

/// <summary>
/// The core permission decision, expressed as a pure function over an in-memory policy
/// snapshot so it can be reasoned about and tested without a database.
/// </summary>
public static class PermissionEvaluator
{
    /// <summary>
    /// Decides whether <paramref name="subject"/> may perform <paramref name="action"/> on
    /// <paramref name="resource"/> under <paramref name="policy"/>.
    /// <para>
    /// A subject's applicable grants are those attached directly to the subject plus those
    /// attached to any role the subject holds, including roles inherited through parents.
    /// Among the grants matching the action and resource, an explicit <c>Deny</c> beats an
    /// <c>Allow</c>, and if nothing matches the default is <c>Deny</c> (deny by default).
    /// </para>
    /// </summary>
    public static GrantEffect Decide(PolicySnapshot policy, string subject, string action, string resource)
    {
        // Every name that can own a grant applying to this subject: the subject itself,
        // plus the transitive closure of the subject's roles over the parent relation.
        var subjectNames = ResolveSubjectNames(policy, subject);

        var matched = false;
        foreach (var grant in policy.Grants)
        {
            if (grant.Action != action || grant.Resource != resource)
            {
                continue;
            }

            if (!subjectNames.Contains(grant.Subject))
            {
                continue;
            }

            // An explicit deny is decisive: it can never be overridden by an allow.
            if (grant.Effect == GrantEffect.Deny)
            {
                return GrantEffect.Deny;
            }

            matched = true;
        }

        // A matching allow (with no deny) grants access; otherwise deny by default.
        return matched ? GrantEffect.Allow : GrantEffect.Deny;
    }

    /// <summary>
    /// The subject id together with every role it holds, walking parent links transitively.
    /// A <see cref="HashSet{T}"/> of visited roles guards against cycles and caps the walk,
    /// so a role graph with a loop terminates instead of spinning forever.
    /// </summary>
    private static HashSet<string> ResolveSubjectNames(PolicySnapshot policy, string subject)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { subject };

        var pending = new Queue<string>();
        foreach (var role in policy.RolesFor(subject))
        {
            if (names.Add(role))
            {
                pending.Enqueue(role);
            }
        }

        while (pending.Count > 0)
        {
            var role = pending.Dequeue();
            if (policy.ParentOf(role) is { } parent && names.Add(parent))
            {
                pending.Enqueue(parent);
            }
        }

        return names;
    }
}
