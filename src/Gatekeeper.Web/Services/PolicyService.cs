using System.Globalization;
using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>
/// The heart of Gatekeeper: answers whether a subject may take an action on a resource,
/// records every decision, and lets callers replace the whole policy in one shot.
/// </summary>
public interface IPolicyService
{
    /// <summary>Decides Allow/Deny for the given subject/action/resource and records the decision.</summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Returns recorded decisions for a subject and/or resource, oldest first.</summary>
    Task<IReadOnlyList<DecisionLog>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default);

    /// <summary>Replaces the entire policy (roles, assignments, grants) with the supplied one.</summary>
    Task ReplacePolicyAsync(PolicyRequest policy, CancellationToken ct = default);
}

public class PolicyService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPolicyService
{
    /// <summary>
    /// All policy managed through the HTTP API lives under one well-known tenant, so the
    /// decision engine and the console share the same Role/UserRole/Grant tables.
    /// </summary>
    public const string ApiCustomerName = "API Policy";

    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var effect = await EvaluateAsync(db, subject, action, resource, ct);

        // Record the decision that was actually returned. The audit log must reflect the
        // real answer — a Deny recorded as Allow would make the service lie about itself.
        db.DecisionLog.Add(new DecisionLog
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = effect,
            Timestamp = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return effect;
    }

    private static async Task<GrantEffect> EvaluateAsync(
        GatekeeperDbContext db, string subject, string action, string resource, CancellationToken ct)
    {
        var customerId = await db.Customers
            .Where(c => c.Name == ApiCustomerName)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // No policy has been loaded yet: default deny.
        if (customerId is null)
        {
            return GrantEffect.Deny;
        }

        // Roles the subject picks up: those assigned directly, plus every transitive parent.
        var directRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == subject)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var roles = await db.Roles
            .Where(r => r.CustomerId == customerId)
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToListAsync(ct);
        var parentOf = roles.ToDictionary(r => r.Id, r => r.ParentRoleId);

        var applicableRoleIds = new HashSet<Guid>();
        var pending = new Queue<Guid>(directRoleIds);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            // Add returns false if already visited — this also breaks any parent cycle.
            if (!applicableRoleIds.Add(id))
            {
                continue;
            }
            if (parentOf.TryGetValue(id, out var parent) && parent is Guid parentId)
            {
                pending.Enqueue(parentId);
            }
        }

        // Every grant that could apply to this subject: its own grants plus its roles' grants.
        var applicableRoleIdList = applicableRoleIds.ToList();
        var candidateGrants = await db.Grants
            .Where(g => g.CustomerId == customerId)
            .Where(g => g.UserId == subject || (g.RoleId != null && applicableRoleIdList.Contains(g.RoleId.Value)))
            .Select(g => new { g.Effect, g.Permission, g.Resource })
            .ToListAsync(ct);

        var matching = candidateGrants
            .Where(g => Matches(g.Permission, action) && Matches(g.Resource, resource))
            .ToList();

        // Deny overrides Allow; no matching grant means deny by default.
        if (matching.Any(g => g.Effect == GrantEffect.Deny))
        {
            return GrantEffect.Deny;
        }
        if (matching.Any(g => g.Effect == GrantEffect.Allow))
        {
            return GrantEffect.Allow;
        }
        return GrantEffect.Deny;
    }

    /// <summary>
    /// Matches a grant pattern against a concrete value. Supports an exact match, the
    /// catch-all "*", and a trailing ":*" prefix wildcard (e.g. "invoice:*" matches "invoice:42").
    /// </summary>
    private static bool Matches(string pattern, string value)
    {
        if (string.Equals(pattern, value, StringComparison.Ordinal))
        {
            return true;
        }
        if (pattern == "*")
        {
            return true;
        }
        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1]; // keep the trailing ':'
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }
        return false;
    }

    public async Task<IReadOnlyList<DecisionLog>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.DecisionLog.AsQueryable();
        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(d => d.Subject == subject);
        }
        if (!string.IsNullOrEmpty(resource))
        {
            query = query.Where(d => d.Resource == resource);
        }

        // Id is store-generated and monotonic, so ordering by it yields true insertion order.
        return await query.OrderBy(d => d.Id).ToListAsync(ct);
    }

    public async Task ReplacePolicyAsync(PolicyRequest policy, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var customerId = await EnsureApiCustomerAsync(db, ct);

        await WipeExistingPolicyAsync(db, customerId, ct);

        // --- Roles: create them, then wire up parents in a second pass. ---
        var roleByName = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var rd in policy.Roles ?? [])
        {
            if (string.IsNullOrWhiteSpace(rd.Name) || roleByName.ContainsKey(rd.Name))
            {
                continue;
            }
            var role = new Role { CustomerId = customerId, Name = rd.Name };
            roleByName[rd.Name] = role;
            db.Roles.Add(role);
        }
        await db.SaveChangesAsync(ct); // assigns role ids

        foreach (var rd in policy.Roles ?? [])
        {
            if (rd.Parent is null || !roleByName.TryGetValue(rd.Name, out var role))
            {
                continue;
            }
            if (roleByName.TryGetValue(rd.Parent, out var parent))
            {
                role.ParentRoleId = parent.Id;
            }
        }

        // Users referenced by assignments/grants may not exist yet; create lightweight rows.
        var ensuredUsers = new HashSet<string>(StringComparer.Ordinal);

        // --- Assignments ---
        foreach (var a in policy.Assignments ?? [])
        {
            if (string.IsNullOrWhiteSpace(a.Subject) || !roleByName.TryGetValue(a.Role, out var role))
            {
                continue;
            }
            await EnsureUserAsync(db, customerId, a.Subject, ensuredUsers, ct);
            db.UserRoles.Add(new UserRole { UserId = a.Subject, RoleId = role.Id });
        }

        // --- Grants: subject is either a known role name or a user id. ---
        foreach (var g in policy.Grants ?? [])
        {
            if (string.IsNullOrWhiteSpace(g.Subject))
            {
                continue;
            }
            var effect = ParseEffect(g.Effect);
            var permission = g.Action ?? string.Empty;
            var resource = string.IsNullOrWhiteSpace(g.Resource) ? "*" : g.Resource;

            if (roleByName.TryGetValue(g.Subject, out var role))
            {
                db.Grants.Add(new Grant
                {
                    CustomerId = customerId,
                    RoleId = role.Id,
                    Effect = effect,
                    Permission = permission,
                    Resource = resource,
                });
            }
            else
            {
                await EnsureUserAsync(db, customerId, g.Subject, ensuredUsers, ct);
                db.Grants.Add(new Grant
                {
                    CustomerId = customerId,
                    UserId = g.Subject,
                    Effect = effect,
                    Permission = permission,
                    Resource = resource,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task<Guid> EnsureApiCustomerAsync(GatekeeperDbContext db, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Name == ApiCustomerName, ct);
        if (customer is null)
        {
            customer = new Customer { Name = ApiCustomerName, PlanTier = PlanTier.Enterprise };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
        }
        return customer.Id;
    }

    private static async Task WipeExistingPolicyAsync(GatekeeperDbContext db, Guid customerId, CancellationToken ct)
    {
        var existingRoles = await db.Roles.Where(r => r.CustomerId == customerId).ToListAsync(ct);
        var existingRoleIds = existingRoles.Select(r => r.Id).ToList();

        var existingGrants = await db.Grants.Where(g => g.CustomerId == customerId).ToListAsync(ct);
        db.Grants.RemoveRange(existingGrants);

        var existingUserRoles = await db.UserRoles
            .Where(ur => existingRoleIds.Contains(ur.RoleId))
            .ToListAsync(ct);
        db.UserRoles.RemoveRange(existingUserRoles);

        // Break parent links before deleting so the restricted parent FK doesn't block the delete.
        foreach (var role in existingRoles)
        {
            role.ParentRoleId = null;
        }
        await db.SaveChangesAsync(ct);

        db.Roles.RemoveRange(existingRoles);
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureUserAsync(
        GatekeeperDbContext db, Guid customerId, string userId, HashSet<string> ensured, CancellationToken ct)
    {
        if (!ensured.Add(userId))
        {
            return;
        }
        var exists = await db.Users.AnyAsync(u => u.Id == userId, ct);
        if (!exists)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = userId,
                CustomerId = customerId,
            });
        }
    }

    private static GrantEffect ParseEffect(string? effect) =>
        Enum.TryParse<GrantEffect>(effect, ignoreCase: true, out var e) ? e : GrantEffect.Deny;

    /// <summary>Formats a timestamp for the wire, e.g. "2026-07-12T18:03:11Z".</summary>
    public static string FormatTimestamp(DateTimeOffset ts) =>
        ts.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
