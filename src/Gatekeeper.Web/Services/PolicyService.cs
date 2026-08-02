using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>Thrown when a submitted policy is internally inconsistent (unknown role, bad effect, cycle).</summary>
public class PolicyValidationException(string message) : Exception(message);

/// <summary>
/// The heart of Gatekeeper: turns stored users, roles and grants into a yes/no answer, and
/// replaces the whole policy in one shot. Every decision is written to the decision audit log.
/// </summary>
public interface IPolicyService
{
    /// <summary>Decide whether <paramref name="subject"/> may take <paramref name="action"/> on
    /// <paramref name="resource"/>, and record the decision in the audit log.</summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource);

    /// <summary>Replace the entire policy (roles, assignments, grants) managed by the API.</summary>
    Task ReplaceAsync(PolicyDto policy);

    /// <summary>Return decision audit entries, oldest first, optionally filtered by subject/resource.</summary>
    Task<IReadOnlyList<DecisionAuditEntry>> GetAuditAsync(string? subject, string? resource);
}

public class PolicyService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPolicyService
{
    /// <summary>Tenant that owns everything created through the HTTP policy API. Kept separate
    /// from any console-seeded tenant so a policy replace never clobbers console data.</summary>
    private const string ApiCustomerName = "Policy API";

    /// <summary>Belt-and-braces bound on role-graph traversal in case a cycle slips into the data.</summary>
    private const int MaxRoleDepth = 1000;

    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Every role the subject holds, directly assigned plus every ancestor reached through
        // parent links. The subject inherits whatever those roles grant.
        var directRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == subject)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var parentOf = await db.Roles
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToDictionaryAsync(r => r.Id, r => r.ParentRoleId);

        var roleIds = ExpandRoles(directRoleIds, parentOf);

        // Every grant that could speak to this request: attached to the subject directly, or to
        // one of the subject's (inherited) roles, and naming this exact action.
        var candidates = await db.Grants
            .Where(g => g.Permission == action
                        && (g.UserId == subject
                            || (g.RoleId != null && roleIds.Contains(g.RoleId.Value))))
            .ToListAsync();

        // A grant applies to this resource if it names it exactly or is a wildcard.
        var applicable = candidates
            .Where(g => g.Resource == resource || g.Resource == "*")
            .ToList();

        // Precedence: an explicit Deny always wins over an Allow; with nothing on point we
        // default to Deny. This is computed exactly once and is what both the caller and the
        // audit log see, so the recorded outcome can never disagree with the returned effect.
        GrantEffect effect =
            applicable.Any(g => g.Effect == GrantEffect.Deny) ? GrantEffect.Deny
            : applicable.Any(g => g.Effect == GrantEffect.Allow) ? GrantEffect.Allow
            : GrantEffect.Deny;

        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = effect,
            Timestamp = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return effect;
    }

    public async Task<IReadOnlyList<DecisionAuditEntry>> GetAuditAsync(string? subject, string? resource)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.DecisionAudit.AsQueryable();
        if (!string.IsNullOrEmpty(subject))
            query = query.Where(e => e.Subject == subject);
        if (!string.IsNullOrEmpty(resource))
            query = query.Where(e => e.Resource == resource);

        // Oldest first; Id breaks ties within the same second so the order is stable.
        return await query
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToListAsync();
    }

    public async Task ReplaceAsync(PolicyDto policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var roles = policy.Roles ?? new();
        var assignments = policy.Assignments ?? new();
        var grants = policy.Grants ?? new();

        ValidatePolicy(roles, assignments, grants);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var customer = await GetOrCreateApiCustomerAsync(db);

        // --- Tear down the existing API-managed policy (but never the audit log or users) ---
        var existingRoles = await db.Roles.Where(r => r.CustomerId == customer.Id).ToListAsync();
        var existingRoleIds = existingRoles.Select(r => r.Id).ToList();

        db.Grants.RemoveRange(await db.Grants.Where(g => g.CustomerId == customer.Id).ToListAsync());
        db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => existingRoleIds.Contains(ur.RoleId)).ToListAsync());
        await db.SaveChangesAsync();

        // Break parent links before deleting so the Restrict FK on Role.Parent can't block us.
        foreach (var r in existingRoles) r.ParentRoleId = null;
        await db.SaveChangesAsync();
        db.Roles.RemoveRange(existingRoles);
        await db.SaveChangesAsync();

        // --- Rebuild ---
        var roleByName = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var rd in roles)
        {
            var role = new Role { CustomerId = customer.Id, Name = rd.Name };
            roleByName[rd.Name] = role;
            db.Roles.Add(role);
        }
        foreach (var rd in roles)
        {
            if (!string.IsNullOrWhiteSpace(rd.Parent))
                roleByName[rd.Name].Parent = roleByName[rd.Parent!];
        }
        await db.SaveChangesAsync();

        var ensuredUsers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var a in assignments)
        {
            await EnsureUserAsync(db, customer, a.Subject, ensuredUsers);
            db.UserRoles.Add(new UserRole { UserId = a.Subject, RoleId = roleByName[a.Role].Id });
        }

        foreach (var g in grants)
        {
            var grant = new Grant
            {
                CustomerId = customer.Id,
                Effect = ParseEffect(g.Effect),
                Permission = g.Action,
                Resource = g.Resource,
            };

            // A grant's subject is either a role name or a user id; a name match makes it a role grant.
            if (roleByName.TryGetValue(g.Subject, out var role))
            {
                grant.RoleId = role.Id;
            }
            else
            {
                await EnsureUserAsync(db, customer, g.Subject, ensuredUsers);
                grant.UserId = g.Subject;
            }

            db.Grants.Add(grant);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // --- helpers ---

    /// <summary>Collect a role set with its full ancestor closure, guarding against cycles.</summary>
    private static HashSet<Guid> ExpandRoles(IEnumerable<Guid> directRoleIds, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var result = new HashSet<Guid>();
        var stack = new Stack<Guid>(directRoleIds);
        var guard = 0;

        while (stack.Count > 0 && guard++ < MaxRoleDepth)
        {
            var id = stack.Pop();
            if (!result.Add(id))
                continue; // already seen: also breaks any parent cycle
            if (parentOf.TryGetValue(id, out var parent) && parent is Guid parentId)
                stack.Push(parentId);
        }

        return result;
    }

    private static void ValidatePolicy(
        List<PolicyRoleDto> roles, List<PolicyAssignmentDto> assignments, List<PolicyGrantDto> grants)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in roles)
        {
            if (string.IsNullOrWhiteSpace(r.Name))
                throw new PolicyValidationException("A role is missing a name.");
            if (!names.Add(r.Name))
                throw new PolicyValidationException($"Duplicate role name '{r.Name}'.");
        }

        foreach (var r in roles)
        {
            if (!string.IsNullOrWhiteSpace(r.Parent) && !names.Contains(r.Parent!))
                throw new PolicyValidationException($"Role '{r.Name}' names unknown parent '{r.Parent}'.");
        }

        // Reject parent cycles up front so we never persist an unresolvable graph.
        var parentOf = roles.ToDictionary(r => r.Name, r => r.Parent, StringComparer.Ordinal);
        foreach (var start in parentOf.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cur = start;
            while (cur is not null && !string.IsNullOrWhiteSpace(cur))
            {
                if (!seen.Add(cur))
                    throw new PolicyValidationException($"Role hierarchy contains a cycle involving '{cur}'.");
                cur = parentOf.TryGetValue(cur, out var p) ? p : null;
            }
        }

        foreach (var a in assignments)
        {
            if (string.IsNullOrWhiteSpace(a.Subject))
                throw new PolicyValidationException("An assignment is missing a subject.");
            if (string.IsNullOrWhiteSpace(a.Role) || !names.Contains(a.Role))
                throw new PolicyValidationException($"Assignment for '{a.Subject}' names unknown role '{a.Role}'.");
        }

        foreach (var g in grants)
        {
            if (string.IsNullOrWhiteSpace(g.Subject))
                throw new PolicyValidationException("A grant is missing a subject.");
            if (string.IsNullOrWhiteSpace(g.Action))
                throw new PolicyValidationException($"Grant for '{g.Subject}' is missing an action.");
            if (string.IsNullOrWhiteSpace(g.Resource))
                throw new PolicyValidationException($"Grant for '{g.Subject}' is missing a resource.");
            _ = ParseEffect(g.Effect); // validates
        }
    }

    private static GrantEffect ParseEffect(string? effect) =>
        Enum.TryParse<GrantEffect>(effect, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new PolicyValidationException($"Grant effect must be 'Allow' or 'Deny', got '{effect}'.");

    private async Task<Customer> GetOrCreateApiCustomerAsync(GatekeeperDbContext db)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Name == ApiCustomerName);
        if (customer is null)
        {
            customer = new Customer { Name = ApiCustomerName, PlanTier = PlanTier.Enterprise };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }
        return customer;
    }

    /// <summary>Ensure an <see cref="ApplicationUser"/> row exists for a subject id so grant/assignment
    /// foreign keys resolve. Users are never deleted on replace; we only create missing ones.</summary>
    private static async Task EnsureUserAsync(
        GatekeeperDbContext db, Customer customer, string subject, HashSet<string> ensured)
    {
        if (!ensured.Add(subject))
            return;
        if (await db.Users.AnyAsync(u => u.Id == subject))
            return;

        db.Users.Add(new ApplicationUser
        {
            Id = subject,
            UserName = subject,
            NormalizedUserName = subject.ToUpperInvariant(),
            CustomerId = customer.Id,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        });
    }
}

// --- Wire-contract DTOs (property names bind case-insensitively from the JSON body) ---

public record DecisionRequest(string Subject, string Action, string Resource);
public record DecisionResponse(string Effect);

public record AuditEntryDto(string Subject, string Action, string Resource, string Outcome, string Timestamp);
public record AuditListResponse(IReadOnlyList<AuditEntryDto> Entries);

public record PolicyDto(List<PolicyRoleDto> Roles, List<PolicyAssignmentDto> Assignments, List<PolicyGrantDto> Grants);
public record PolicyRoleDto(string Name, string? Parent);
public record PolicyAssignmentDto(string Subject, string Role);
public record PolicyGrantDto(string Subject, string Action, string Resource, string Effect);
