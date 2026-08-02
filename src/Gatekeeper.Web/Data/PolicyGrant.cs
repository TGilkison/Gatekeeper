namespace Gatekeeper.Web.Data;

/// <summary>
/// An allow/deny of an action on a resource in the HTTP API policy. A grant's
/// <see cref="Subject"/> is either a user id (e.g. "user-42") or a role name (e.g. "Staff").
/// </summary>
public class PolicyGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>A user id or a role name the grant hangs off of.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action being controlled, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action applies to, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>Whether this grant allows or denies.</summary>
    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}
