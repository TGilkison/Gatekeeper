namespace Gatekeeper.Web.Data;

/// <summary>
/// An allow/deny of an action on a resource in the HTTP authorization policy. A grant's
/// <see cref="Subject"/> is either a user id (e.g. "user-42") or a role name (e.g. "Staff");
/// which one is resolved at decision time against the known role names.
/// </summary>
public class PolicyGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>A user id or a role name.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action being controlled, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action applies to, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>Whether this grant allows or denies.</summary>
    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}
