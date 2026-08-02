namespace Gatekeeper.Web.Data;

/// <summary>Assignment of an access-control <see cref="Role"/> to an <see cref="ApplicationUser"/>.</summary>
public class UserRole
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
