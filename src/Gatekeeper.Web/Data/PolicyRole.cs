namespace Gatekeeper.Web.Data;

/// <summary>
/// A role in the authorization policy exposed over the HTTP API. Distinct from the
/// console's <see cref="Role"/>: the public API works in opaque role names with no
/// tenant scoping, so it gets its own flat, self-contained table.
/// </summary>
public class PolicyRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The role's unique name, e.g. "Editor".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent role this role inherits from, or null for no parent.</summary>
    public string? ParentName { get; set; }
}
