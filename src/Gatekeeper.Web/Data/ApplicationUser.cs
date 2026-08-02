using Microsoft.AspNetCore.Identity;

namespace Gatekeeper.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    /// <summary>The tenant this user belongs to. Nullable so the very first admin can exist before any customer.</summary>
    public Guid? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Access-control roles assigned to this user (distinct from Identity roles).</summary>
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>Grants that hang directly off this user.</summary>
    public ICollection<Grant> Grants { get; set; } = new List<Grant>();
}
