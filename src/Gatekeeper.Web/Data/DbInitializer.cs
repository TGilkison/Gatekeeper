using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Data;

/// <summary>Applies migrations and seeds a demo admin, tenant, roles and grants on first run.</summary>
public static class DbInitializer
{
    public const string AdminEmail = "admin@gatekeeper.local";
    public const string AdminPassword = "Admin!23456";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<GatekeeperDbContext>();
        await db.Database.MigrateAsync();

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Seed a sample tenant so there is something to look at in the console.
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Name == "Acme Inc");
        if (customer is null)
        {
            customer = new Customer { Name = "Acme Inc", PlanTier = PlanTier.Pro };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        // Seed the console admin login.
        var admin = await userManager.FindByEmailAsync(AdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                EmailConfirmed = true,
                DisplayName = "Console Admin",
                CustomerId = customer.Id,
            };
            await userManager.CreateAsync(admin, AdminPassword);
        }

        // Seed a small role hierarchy and a couple of grants.
        if (!await db.Roles.AnyAsync(r => r.CustomerId == customer.Id))
        {
            var member = new Role
            {
                CustomerId = customer.Id,
                Name = "Member",
                Description = "Base role every user inherits from.",
            };
            var manager = new Role
            {
                CustomerId = customer.Id,
                Name = "Manager",
                Description = "Team managers. Inherits Member.",
                Parent = member,
            };
            db.Roles.AddRange(member, manager);

            db.Grants.AddRange(
                new Grant { CustomerId = customer.Id, Role = member, Effect = GrantEffect.Allow, Permission = "documents.read", Resource = "*" },
                new Grant { CustomerId = customer.Id, Role = manager, Effect = GrantEffect.Allow, Permission = "documents.write", Resource = "*" },
                new Grant { CustomerId = customer.Id, Role = manager, Effect = GrantEffect.Deny, Permission = "billing.manage", Resource = "*" });

            await db.SaveChangesAsync();
        }
    }
}
