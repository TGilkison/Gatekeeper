namespace Gatekeeper.Web.Data;

/// <summary>
/// A role in the decision engine's policy. Roles nest via <see cref="ParentName"/> so a
/// subject picks up every grant on the role and, transitively, on its ancestors.
/// This is the tenant-agnostic policy the HTTP API owns (see PUT /api/policy), distinct
/// from the tenant-scoped <see cref="Role"/> the admin console manages.
/// </summary>
public class PolicyRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique role name, e.g. "Editor".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent role, or null when the role has no parent.</summary>
    public string? ParentName { get; set; }
}
