namespace Gatekeeper.Web.Data;

/// <summary>
/// An Allow/Deny of an action on a resource in the decision policy. A grant hangs off a
/// single subject, which is either a user id (e.g. "user-42") or a role name (e.g. "Staff").
/// </summary>
public class PolicyGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Either a user id or a role name.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action being controlled, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action applies to, e.g. "doc-7". "*" matches any resource.</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>Whether this grant allows or denies the action.</summary>
    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}
