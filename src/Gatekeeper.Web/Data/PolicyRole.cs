namespace Gatekeeper.Web.Data;

/// <summary>
/// A role as expressed over the decision API's wire contract: identified by name,
/// with an optional named parent it inherits grants from. This is deliberately
/// separate from the console's <see cref="Role"/> (which is tenant-scoped, keyed by
/// GUID and tied to ASP.NET Identity users). The decision API speaks in opaque
/// string subjects and role names, so it gets its own faithful storage.
/// </summary>
public class PolicyRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The role's unique name, e.g. "Editor".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Name of the parent role, or null when the role has no parent.</summary>
    public string? ParentName { get; set; }
}
