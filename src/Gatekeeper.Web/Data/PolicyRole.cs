namespace Gatekeeper.Web.Data;

/// <summary>
/// A role in the policy served over the HTTP API. Distinct from the console's tenant-scoped
/// <see cref="Role"/>: the API addresses roles by name and lets a subject be either a user id or
/// a role name, so its policy lives in its own flat, string-keyed tables.
/// </summary>
public class PolicyRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Role name, e.g. "Editor". Unique across the policy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent role this role inherits grants from, or null for no parent.</summary>
    public string? ParentName { get; set; }
}
