namespace Gatekeeper.Web.Data;

/// <summary>
/// An access-control role. Roles can nest via <see cref="ParentRoleId"/> so grants
/// can be inherited from a parent. This is deliberately separate from ASP.NET Identity
/// roles, which are only used for console sign-in.
/// </summary>
public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Optional parent role. A role inherits its parent's grants.</summary>
    public Guid? ParentRoleId { get; set; }

    public Role? Parent { get; set; }

    public ICollection<Role> Children { get; set; } = new List<Role>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>Grants that hang off this role.</summary>
    public ICollection<Grant> Grants { get; set; } = new List<Grant>();
}
