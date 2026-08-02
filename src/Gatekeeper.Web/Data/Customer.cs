namespace Gatekeeper.Web.Data;

/// <summary>A tenant. Everything else in Gatekeeper is scoped to a customer.</summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public PlanTier PlanTier { get; set; } = PlanTier.Free;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

    public ICollection<Role> Roles { get; set; } = new List<Role>();

    public ICollection<Grant> Grants { get; set; } = new List<Grant>();
}
