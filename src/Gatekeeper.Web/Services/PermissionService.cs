using Gatekeeper.Web.Api;
using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>Raised when a PUT /api/policy body is malformed; surfaced to the caller as 400.</summary>
public sealed class PolicyValidationException(string message) : Exception(message);

/// <summary>
/// Answers the question Gatekeeper exists to answer — may this subject take this action on
/// this resource — and owns the policy the answer is derived from.
/// </summary>
public interface IPermissionService
{
    /// <summary>Decides an access request, writes the decision to the audit log, and returns the effect.</summary>
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);

    /// <summary>Replaces the entire policy (roles, assignments, grants) in one shot.</summary>
    Task ReplacePolicyAsync(PolicyRequest policy, CancellationToken ct = default);

    /// <summary>Returns audit entries for a subject/resource, oldest first. A null filter is not applied.</summary>
    Task<IReadOnlyList<DecisionLogEntry>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default);
}

public sealed class PermissionService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IPermissionService
{
    // The HTTP API manages a single, self-contained policy. It lives under its own tenant so a
    // full replace never touches the seeded console demo data (or the console admin login).
    private const string ApiTenantName = "__api__";

    public async Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenantId = await GetTenantIdAsync(db, ct);

        // Compute the effect exactly once, then record and return that same value. There is no
        // second, separate path that could disagree with what the caller is told.
        var effect = tenantId is Guid id
            ? await EvaluateAsync(db, id, subject, action, resource, ct)
            : GrantEffect.Deny; // no policy has ever been stored -> deny by default

        db.DecisionLog.Add(new DecisionLogEntry
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
        GatekeeperDbContext db, Guid tenantId, string subject, string action, string resource, CancellationToken ct)
    {
        // The roles this subject holds directly...
        var assignedRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == subject)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        // ...expanded up the parent chain, so the subject picks up everything their roles inherit.
        var parentOf = await db.Roles
            .Where(r => r.CustomerId == tenantId)
            .Select(r => new { r.Id, r.ParentRoleId })
            .ToDictionaryAsync(r => r.Id, r => r.ParentRoleId, ct);

        var effectiveRoleIds = ExpandRoles(assignedRoleIds, parentOf);

        // Every grant that could bear on this request: the subject's own grants, plus grants on
        // any role they effectively hold. Match the action exactly; a grant on "*" covers any
        // resource, otherwise the resource must match exactly.
        var candidates = await db.Grants
            .Where(g => g.CustomerId == tenantId
                && g.Permission == action
                && (g.Resource == resource || g.Resource == "*")
                && (g.UserId == subject
                    || (g.RoleId != null && effectiveRoleIds.Contains(g.RoleId.Value))))
            .Select(g => g.Effect)
            .ToListAsync(ct);

        // Precedence: an explicit Deny always wins over an Allow. With no matching grant at all,
        // the answer is Deny (default-deny). These three cases are kept distinct on purpose.
        if (candidates.Contains(GrantEffect.Deny))
            return GrantEffect.Deny;
        if (candidates.Contains(GrantEffect.Allow))
            return GrantEffect.Allow;
        return GrantEffect.Deny;
    }

    /// <summary>
    /// Returns the starting roles plus all their ancestors. The visited set makes this safe
    /// against cycles and self-references in the role graph (it cannot loop forever).
    /// </summary>
    private static HashSet<Guid> ExpandRoles(IEnumerable<Guid> start, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var result = new HashSet<Guid>();
        var stack = new Stack<Guid>(start);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!result.Add(id))
                continue; // already seen -> stops cycles
            if (parentOf.TryGetValue(id, out var parent) && parent is Guid parentId)
                stack.Push(parentId);
        }
        return result;
    }

    public async Task<IReadOnlyList<DecisionLogEntry>> GetAuditAsync(string? subject, string? resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        IQueryable<DecisionLogEntry> query = db.DecisionLog;
        if (!string.IsNullOrEmpty(subject))
            query = query.Where(e => e.Subject == subject);
        if (!string.IsNullOrEmpty(resource))
            query = query.Where(e => e.Resource == resource);

        return await query
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
    }

    public async Task ReplacePolicyAsync(PolicyRequest policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var roleDtos = policy.Roles ?? [];
        var assignmentDtos = policy.Assignments ?? [];
        var grantDtos = policy.Grants ?? [];

        Validate(roleDtos, assignmentDtos, grantDtos);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var tenant = await GetOrCreateTenantAsync(db, ct);

        // --- Clear the existing policy for this tenant (users are kept; they are referenced, not owned). ---
        var oldRoles = await db.Roles.Where(r => r.CustomerId == tenant.Id).ToListAsync(ct);
        var oldRoleIds = oldRoles.Select(r => r.Id).ToList();

        db.Grants.RemoveRange(await db.Grants.Where(g => g.CustomerId == tenant.Id).ToListAsync(ct));
        db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => oldRoleIds.Contains(ur.RoleId)).ToListAsync(ct));
        // Break parent links before deleting roles: the parent FK is Restrict, so a parent
        // cannot be removed while a child still points at it.
        foreach (var r in oldRoles)
            r.ParentRoleId = null;
        await db.SaveChangesAsync(ct);

        db.Roles.RemoveRange(oldRoles);
        await db.SaveChangesAsync(ct);

        // --- Recreate roles. ---
        var roleByName = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var dto in roleDtos)
        {
            var role = new Role { CustomerId = tenant.Id, Name = dto.Name! };
            roleByName[dto.Name!] = role;
            db.Roles.Add(role);
        }
        foreach (var dto in roleDtos)
        {
            if (string.IsNullOrEmpty(dto.Parent))
                continue;
            if (!roleByName.TryGetValue(dto.Parent, out var parent))
                throw new PolicyValidationException($"Role '{dto.Name}' names unknown parent '{dto.Parent}'.");
            roleByName[dto.Name!].Parent = parent;
        }
        await db.SaveChangesAsync(ct); // assigns role ids

        // --- Ensure a user row exists for every subject that is a user (not a role name). ---
        var roleNames = new HashSet<string>(roleByName.Keys, StringComparer.Ordinal);
        var userSubjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in assignmentDtos)
            userSubjects.Add(a.Subject!);
        foreach (var g in grantDtos)
            if (!roleNames.Contains(g.Subject!))
                userSubjects.Add(g.Subject!);

        foreach (var subject in userSubjects)
        {
            var existing = await db.Users.FindAsync([subject], ct);
            if (existing is null)
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = subject,
                    UserName = subject,
                    NormalizedUserName = subject.ToUpperInvariant(),
                    CustomerId = tenant.Id,
                    SecurityStamp = Guid.NewGuid().ToString(),
                });
            }
            else if (existing.CustomerId is null)
            {
                existing.CustomerId = tenant.Id;
            }
        }
        await db.SaveChangesAsync(ct);

        // --- Recreate assignments. ---
        foreach (var a in assignmentDtos)
        {
            if (!roleByName.TryGetValue(a.Role!, out var role))
                throw new PolicyValidationException($"Assignment for '{a.Subject}' names unknown role '{a.Role}'.");
            db.UserRoles.Add(new UserRole { UserId = a.Subject!, RoleId = role.Id });
        }

        // --- Recreate grants (each hangs off a role if the subject is a role name, else a user). ---
        foreach (var g in grantDtos)
        {
            var grant = new Grant
            {
                CustomerId = tenant.Id,
                Effect = ParseEffect(g.Effect),
                Permission = g.Action!,
                Resource = g.Resource!,
            };
            if (roleByName.TryGetValue(g.Subject!, out var role))
                grant.RoleId = role.Id;
            else
                grant.UserId = g.Subject!;
            db.Grants.Add(grant);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static void Validate(
        IReadOnlyList<PolicyRoleDto> roles,
        IReadOnlyList<PolicyAssignmentDto> assignments,
        IReadOnlyList<PolicyGrantDto> grants)
    {
        foreach (var r in roles)
            if (string.IsNullOrWhiteSpace(r.Name))
                throw new PolicyValidationException("Every role must have a name.");

        var duplicate = roles.GroupBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new PolicyValidationException($"Duplicate role name '{duplicate.Key}'.");

        foreach (var a in assignments)
        {
            if (string.IsNullOrWhiteSpace(a.Subject))
                throw new PolicyValidationException("Every assignment must have a subject.");
            if (string.IsNullOrWhiteSpace(a.Role))
                throw new PolicyValidationException("Every assignment must name a role.");
        }

        foreach (var g in grants)
        {
            if (string.IsNullOrWhiteSpace(g.Subject))
                throw new PolicyValidationException("Every grant must have a subject.");
            if (string.IsNullOrWhiteSpace(g.Action))
                throw new PolicyValidationException("Every grant must have an action.");
            if (string.IsNullOrWhiteSpace(g.Resource))
                throw new PolicyValidationException("Every grant must have a resource.");
            _ = ParseEffect(g.Effect); // validates the effect string
        }
    }

    private static GrantEffect ParseEffect(string? effect) => effect?.Trim().ToLowerInvariant() switch
    {
        "allow" => GrantEffect.Allow,
        "deny" => GrantEffect.Deny,
        _ => throw new PolicyValidationException($"Grant effect must be 'Allow' or 'Deny', got '{effect}'."),
    };

    private static async Task<Guid?> GetTenantIdAsync(GatekeeperDbContext db, CancellationToken ct)
    {
        var tenant = await db.Customers
            .Where(c => c.Name == ApiTenantName)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        return tenant;
    }

    private static async Task<Customer> GetOrCreateTenantAsync(GatekeeperDbContext db, CancellationToken ct)
    {
        var tenant = await db.Customers.FirstOrDefaultAsync(c => c.Name == ApiTenantName, ct);
        if (tenant is null)
        {
            tenant = new Customer { Name = ApiTenantName, PlanTier = PlanTier.Enterprise };
            db.Customers.Add(tenant);
            await db.SaveChangesAsync(ct);
        }
        return tenant;
    }
}
