using Gatekeeper.Web.Data;

namespace Gatekeeper.Web.Authorization;

// The authorization policy that the HTTP decision API evaluates. It is deliberately
// self-contained and driven entirely by PUT /api/policy: subjects are opaque strings
// (a user id or a role name), with no dependency on ASP.NET Identity or tenancy. This
// keeps the decision path a clean, replaceable document, exactly as the wire contract
// describes it, and leaves the console's Identity-backed management untouched.

/// <summary>A role in the policy graph. A role may inherit from a single parent by name.</summary>
public class PolicyRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique role name, e.g. "Editor". This is how grants and assignments refer to the role.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent role this role inherits grants from, or null for a root role.</summary>
    public string? ParentName { get; set; }
}

/// <summary>Assigns a subject (a user id) one of the policy roles.</summary>
public class RoleAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user id being assigned a role.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The <see cref="PolicyRole.Name"/> granted to the subject.</summary>
    public string RoleName { get; set; } = string.Empty;
}

/// <summary>
/// An allow or deny of an action on a resource. The <see cref="Subject"/> is either a user id
/// or a role name; a role grant applies to every user who holds that role (directly or by inheritance).
/// </summary>
public class PolicyGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>A user id or a role name.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action being controlled, e.g. "document:delete". "*" matches any action.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the grant applies to, e.g. "doc-7". "*" matches any resource.</summary>
    public string Resource { get; set; } = "*";

    public GrantEffect Effect { get; set; } = GrantEffect.Allow;
}

/// <summary>
/// An append-only record of one authorization decision. Every call to the decision endpoint
/// writes exactly one of these, capturing the outcome actually returned to the caller.
/// </summary>
public class DecisionAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Subject { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    /// <summary>The effect returned to the caller: Allow or Deny.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
