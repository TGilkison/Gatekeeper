namespace Gatekeeper.Web.Data;

/// <summary>
/// An Allow/Deny of an action on a resource, over the decision API. A grant's
/// <see cref="Subject"/> is either a user id or a role name; which one is determined
/// at decision time by whether the subject matches a declared <see cref="PolicyRole"/>.
/// </summary>
public class PolicyGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Either a user id (e.g. "user-42") or a role name (e.g. "Staff").</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action being controlled, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action applies to, e.g. "doc-7". "*" matches any resource.</summary>
    public string Resource { get; set; } = string.Empty;

    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}
