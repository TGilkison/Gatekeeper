using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Data;

public class GatekeeperDbContext(DbContextOptions<GatekeeperDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    // 'new' because IdentityDbContext already exposes Roles/UserRoles for its own
    // IdentityRole tables. Gatekeeper's access-control roles are a separate concept.
    public new DbSet<Role> Roles => Set<Role>();
    public new DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Grant> Grants => Set<Grant>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<DecisionAuditEntry> DecisionAudit => Set<DecisionAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Store enums as readable text in the database rather than ints.
        builder.Entity<Customer>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.PlanTier).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.HasOne(u => u.Customer)
                .WithMany(c => c.Users)
                .HasForeignKey(u => u.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Role>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(200).IsRequired();
            e.Property(r => r.Description).HasMaxLength(1000);

            e.HasOne(r => r.Customer)
                .WithMany(c => c.Roles)
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Parent)
                .WithMany(r => r.Children)
                .HasForeignKey(r => r.ParentRoleId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.CustomerId, r.Name }).IsUnique();
        });

        builder.Entity<UserRole>(e =>
        {
            e.HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
        });

        builder.Entity<Grant>(e =>
        {
            e.Property(g => g.Effect).HasConversion<string>().HasMaxLength(10);
            e.Property(g => g.Permission).HasMaxLength(200).IsRequired();
            e.Property(g => g.Resource).HasMaxLength(200).IsRequired();

            e.HasOne(g => g.Customer)
                .WithMany(c => c.Grants)
                .HasForeignKey(g => g.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(g => g.User)
                .WithMany(u => u.Grants)
                .HasForeignKey(g => g.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(g => g.Role)
                .WithMany(r => r.Grants)
                .HasForeignKey(g => g.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            // A grant must hang off exactly one subject: a user XOR a role.
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Grant_OneSubject",
                "(\"UserId\" IS NOT NULL AND \"RoleId\" IS NULL) OR (\"UserId\" IS NULL AND \"RoleId\" IS NOT NULL)"));

            e.HasIndex(g => new { g.CustomerId, g.Permission, g.Resource });
        });

        builder.Entity<AuditLogEntry>(e =>
        {
            e.Property(a => a.ActorName).HasMaxLength(256).IsRequired();
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            e.Property(a => a.EntityId).HasMaxLength(256).IsRequired();
            e.Property(a => a.Summary).HasMaxLength(2000).IsRequired();
            e.HasIndex(a => a.Timestamp);
        });

        builder.Entity<DecisionAuditEntry>(e =>
        {
            e.Property(d => d.Subject).HasMaxLength(256).IsRequired();
            e.Property(d => d.Action).HasMaxLength(200).IsRequired();
            e.Property(d => d.Resource).HasMaxLength(200).IsRequired();
            e.Property(d => d.Outcome).HasConversion<string>().HasMaxLength(10);

            // The audit page reads by (subject, resource); index it to match.
            e.HasIndex(d => new { d.Subject, d.Resource });
            e.HasIndex(d => d.Timestamp);
        });
    }
}
