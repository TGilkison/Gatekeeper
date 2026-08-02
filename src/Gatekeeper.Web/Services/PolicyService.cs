using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>
/// The heart of Gatekeeper: turns stored policy (roles, role inheritance, assignments and
/// allow/deny grants) into a yes/no answer, records every answer to the decision audit log,
/// and lets callers replace the whole policy in one shot.
/// </summary>
public interface IPolicyService
{
    /// <summary>
    /// Decides whether <paramref name="subject"/> may take <paramref name="action"/> on
    /// <paramref name="resource"/>, and writes the outcome to the audit log.
    /// </summary>
    Task<GrantEffect> EvaluateAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Returns decision-log entries, oldest first, optionally filtered by subject and/or resource.</summary>
    Task<IReadOnlyList<DecisionAuditEntry>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default);

    /// <summary>Atomically replaces the entire policy for the API tenant.</summary>
    Task ReplacePolicyAsync(PolicyDto policy, CancellationToken ct = default);
}

public sealed class PolicyService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPolicyService
{
    // The HTTP API operates as a single tenant. Console-managed tenants (e.g. the seeded
    // "Acme Inc") are left untouched; everything pushed via PUT /api/policy lives here.
    private const string ApiCustomerName = "API Clients";

    public async Task<GrantEffect> EvaluateAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var customerId = await GetOrCreateApiCustomerIdAsync(db, ct);

        var roleClosure = await ResolveRoleClosureAsync(db, customerId, subject, ct);

        // A grant applies if it targets this subject directly or one of the subject's
        // (possibly inherited) roles, and its permission and resource both match.
        var grants = await db.Grants
            .Where(g => g.CustomerId == customerId)
            .ToListAsync(ct);

        var applicable = grants.Where(g =>
                ((g.UserId != null && g.UserId == subject) ||
                 (g.RoleId != null && roleClosure.Contains(g.RoleId.Value)))
                && PermissionMatches(g.Permission, action)
                && ResourceMatches(g.Resource, resource))
            .ToList();

        // Precedence, made explicit:
        //   1. No grant applies            -> Deny (default-deny; the safe answer).
        //   2. Any applicable grant denies -> Deny (an explicit Deny always wins).
        //   3. Otherwise                   -> Allow.
        var effect =
            applicable.Count == 0 ? GrantEffect.Deny
            : applicable.Any(g => g.Effect == GrantEffect.Deny) ? GrantEffect.Deny
            : GrantEffect.Allow;

        // Every decision is recorded, with the outcome we actually returned.
        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            CustomerId = customerId,
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = effect,
        });
        await db.SaveChangesAsync(ct);

        return effect;
    }

    public async Task<IReadOnlyList<DecisionAuditEntry>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.DecisionAudit.AsQueryable();
        if (!string.IsNullOrWhiteSpace(subject))
            query = query.Where(a => a.Subject == subject);
        if (!string.IsNullOrWhiteSpace(resource))
            query = query.Where(a => a.Resource == resource);

        // Oldest entry first, with Id as a stable tie-breaker within the same instant.
        return await query
            .OrderBy(a => a.Timestamp)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);
    }

    public async Task ReplacePolicyAsync(PolicyDto policy, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var customerId = await GetOrCreateApiCustomerIdAsync(db, ct);

        await WipePolicyAsync(db, customerId, ct);

        // --- Roles (first pass: create; second pass: wire up parents) ---
        var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var r in policy.Roles ?? [])
        {
            if (string.IsNullOrWhiteSpace(r.Name))
                throw new PolicyValidationException("Every role must have a name.");
            if (!roles.TryAdd(r.Name, new Role { CustomerId = customerId, Name = r.Name }))
                throw new PolicyValidationException($"Duplicate role '{r.Name}'.");
        }
        db.Roles.AddRange(roles.Values);

        foreach (var r in policy.Roles ?? [])
        {
            if (string.IsNullOrWhiteSpace(r.Parent))
                continue;
            if (!roles.TryGetValue(r.Parent, out var parent))
                throw new PolicyValidationException($"Role '{r.Name}' names unknown parent '{r.Parent}'.");
            roles[r.Name!].Parent = parent;
        }
        GuardAgainstCycles(roles.Values);

        // Subjects that appear anywhere become user stubs so the existing FKs hold and the
        // console can render them. One stub per distinct subject id. Idempotent: if a row for
        // this subject already exists (e.g. left by a prior policy version), adopt it into the
        // API tenant rather than inserting a duplicate.
        var users = new Dictionary<string, ApplicationUser>(StringComparer.Ordinal);
        async Task<ApplicationUser> UserForAsync(string subject)
        {
            if (users.TryGetValue(subject, out var cached))
                return cached;

            var user = await db.Users.FindAsync([subject], ct);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = subject,
                    UserName = subject,
                    NormalizedUserName = subject.ToUpperInvariant(),
                    DisplayName = subject,
                    CustomerId = customerId,
                };
                db.Users.Add(user);
            }
            else
            {
                user.CustomerId = customerId;
            }

            users[subject] = user;
            return user;
        }

        // --- Assignments ---
        foreach (var a in policy.Assignments ?? [])
        {
            if (string.IsNullOrWhiteSpace(a.Subject))
                throw new PolicyValidationException("Every assignment must name a subject.");
            if (string.IsNullOrWhiteSpace(a.Role))
                throw new PolicyValidationException($"Assignment for '{a.Subject}' must name a role.");
            if (!roles.TryGetValue(a.Role, out var role))
                throw new PolicyValidationException($"Assignment for '{a.Subject}' names unknown role '{a.Role}'.");

            db.UserRoles.Add(new UserRole { User = await UserForAsync(a.Subject), Role = role });
        }

        // --- Grants ---
        foreach (var g in policy.Grants ?? [])
        {
            if (string.IsNullOrWhiteSpace(g.Subject))
                throw new PolicyValidationException("Every grant must name a subject.");
            if (string.IsNullOrWhiteSpace(g.Action))
                throw new PolicyValidationException($"Grant for '{g.Subject}' must name an action.");

            var grant = new Grant
            {
                CustomerId = customerId,
                Effect = ParseEffect(g.Effect),
                Permission = g.Action.Trim(),
                Resource = string.IsNullOrWhiteSpace(g.Resource) ? "*" : g.Resource.Trim(),
            };

            // A grant's subject is a role name if it matches a defined role, otherwise a user id.
            if (roles.TryGetValue(g.Subject, out var role))
                grant.Role = role;
            else
                grant.User = await UserForAsync(g.Subject);

            db.Grants.Add(grant);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // --- helpers ---

    /// <summary>Collects the subject's directly assigned roles plus every ancestor via the parent chain.</summary>
    private static async Task<HashSet<Guid>> ResolveRoleClosureAsync(
        GatekeeperDbContext db, Guid customerId, string subject, CancellationToken ct)
    {
        var directRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == subject)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var roles = await db.Roles
            .Where(r => r.CustomerId == customerId)
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToListAsync(ct);
        var parentOf = roles.ToDictionary(r => r.Id, r => r.ParentRoleId);

        // Walk up each chain. The visited set also makes the traversal safe against a
        // cyclic role graph (see V0-NO-CYCLE-DETECTION), so a bad cycle can't hang a decision.
        var closure = new HashSet<Guid>();
        var stack = new Stack<Guid>(directRoleIds);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!closure.Add(id))
                continue;
            if (parentOf.TryGetValue(id, out var parent) && parent is Guid p)
                stack.Push(p);
        }
        return closure;
    }

    private static async Task WipePolicyAsync(GatekeeperDbContext db, Guid customerId, CancellationToken ct)
    {
        var users = await db.Users.Where(u => u.CustomerId == customerId).ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();

        db.Grants.RemoveRange(await db.Grants.Where(g => g.CustomerId == customerId).ToListAsync(ct));
        db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        // Break parent links before deleting so the RESTRICT self-reference FK can't block the delete.
        var roles = await db.Roles.Where(r => r.CustomerId == customerId).ToListAsync(ct);
        foreach (var r in roles)
            r.ParentRoleId = null;
        await db.SaveChangesAsync(ct);

        db.Roles.RemoveRange(roles);
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync(ct);
    }

    private static void GuardAgainstCycles(IEnumerable<Role> roles)
    {
        foreach (var start in roles)
        {
            var seen = new HashSet<Role>();
            for (var cur = start; cur is not null; cur = cur.Parent)
            {
                if (!seen.Add(cur))
                    throw new PolicyValidationException($"Role '{start.Name}' is part of a parent cycle.");
            }
        }
    }

    private static GrantEffect ParseEffect(string? effect) =>
        Enum.TryParse<GrantEffect>(effect, ignoreCase: true, out var e) && Enum.IsDefined(e)
            ? e
            : throw new PolicyValidationException($"Grant effect must be 'Allow' or 'Deny', got '{effect}'.");

    /// <summary>Permission matches on exact equality, or a "*" grant covering every action.</summary>
    private static bool PermissionMatches(string grantPermission, string action) =>
        grantPermission == "*" || grantPermission == action;

    /// <summary>Resource matches exactly, via a "*" wildcard, or a "prefix:*" grant.</summary>
    private static bool ResourceMatches(string grantResource, string resource)
    {
        if (grantResource == "*" || grantResource == resource)
            return true;
        if (grantResource.EndsWith(":*", StringComparison.Ordinal))
            return resource.StartsWith(grantResource[..^1], StringComparison.Ordinal);
        return false;
    }

    private static async Task<Guid> GetOrCreateApiCustomerIdAsync(GatekeeperDbContext db, CancellationToken ct)
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
}
