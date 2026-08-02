using System.Globalization;
using Gatekeeper.Web.Data;
using Gatekeeper.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Endpoints;

/// <summary>
/// The HTTP surface other applications call: ask for a decision, read the decision audit
/// trail, and replace the decision policy.
/// </summary>
public static class DecisionApi
{
    private const string DecisionEntity = "Decision";

    public static IEndpointRouteBuilder MapDecisionApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/decisions", PostDecisionAsync);
        app.MapGet("/api/audit", GetAuditAsync);
        app.MapPut("/api/policy", PutPolicyAsync);
        return app;
    }

    // POST /api/decisions
    private static async Task<IResult> PostDecisionAsync(
        DecisionRequest request,
        IAccessDecider decider,
        IDbContextFactory<GatekeeperDbContext> dbFactory,
        CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Subject)
            || string.IsNullOrWhiteSpace(request.Action)
            || string.IsNullOrWhiteSpace(request.Resource))
        {
            return Results.BadRequest(new { error = "subject, action and resource are all required." });
        }

        var effect = await decider.DecideAsync(request.Subject, request.Action, request.Resource, ct);
        var outcome = effect.ToString();

        // Every decision is written to the audit log, recording the effect actually returned.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditLog.Add(new AuditLogEntry
        {
            ActorName = "api",
            Action = request.Action,
            EntityType = DecisionEntity,
            EntityId = request.Resource,
            Summary = $"{outcome} '{request.Action}' on '{request.Resource}' for '{request.Subject}'",
            Subject = request.Subject,
            Resource = request.Resource,
            Outcome = outcome,
        });
        await db.SaveChangesAsync(ct);

        return Results.Ok(new DecisionResponse(outcome));
    }

    // GET /api/audit?subject={subject}&resource={resource}
    private static async Task<IResult> GetAuditAsync(
        string? subject,
        string? resource,
        IDbContextFactory<GatekeeperDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.AuditLog.Where(e => e.EntityType == DecisionEntity);
        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(e => e.Subject == subject);
        }
        if (!string.IsNullOrEmpty(resource))
        {
            query = query.Where(e => e.Resource == resource);
        }

        var rows = await query
            .OrderBy(e => e.Timestamp) // oldest entry first
            .ToListAsync(ct);

        var entries = rows.Select(e => new AuditEntry(
            e.Subject ?? string.Empty,
            e.Action,
            e.Resource ?? string.Empty,
            e.Outcome ?? string.Empty,
            e.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))
            .ToList();

        return Results.Ok(new AuditResponse(entries));
    }

    // PUT /api/policy — replaces the whole policy in one shot.
    private static async Task<IResult> PutPolicyAsync(
        PolicyRequest request,
        IDbContextFactory<GatekeeperDbContext> dbFactory,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Results.BadRequest(new { error = "A policy body is required." });
        }

        var roles = request.Roles ?? new List<PolicyRoleDto>();
        var assignments = request.Assignments ?? new List<PolicyAssignmentDto>();
        var grants = request.Grants ?? new List<PolicyGrantDto>();

        // Validate up front so a bad grant does not leave the policy half-replaced.
        var parsedGrants = new List<PolicyGrant>(grants.Count);
        foreach (var g in grants)
        {
            if (g is null || string.IsNullOrWhiteSpace(g.Subject) || string.IsNullOrWhiteSpace(g.Action) || string.IsNullOrWhiteSpace(g.Resource))
            {
                return Results.BadRequest(new { error = "Each grant needs subject, action, resource and effect." });
            }
            if (!Enum.TryParse<GrantEffect>(g.Effect, ignoreCase: true, out var effect))
            {
                return Results.BadRequest(new { error = $"Unknown effect '{g.Effect}'. Use 'Allow' or 'Deny'." });
            }
            parsedGrants.Add(new PolicyGrant { Subject = g.Subject, Action = g.Action, Resource = g.Resource, Effect = effect });
        }

        foreach (var r in roles)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.Name))
            {
                return Results.BadRequest(new { error = "Each role needs a name." });
            }
        }
        foreach (var a in assignments)
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Subject) || string.IsNullOrWhiteSpace(a.Role))
            {
                return Results.BadRequest(new { error = "Each assignment needs a subject and a role." });
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Replace the whole policy: clear it out, then write the new one.
        await db.PolicyGrants.ExecuteDeleteAsync(ct);
        await db.PolicyAssignments.ExecuteDeleteAsync(ct);
        await db.PolicyRoles.ExecuteDeleteAsync(ct);

        db.PolicyRoles.AddRange(roles.Select(r => new PolicyRole { Name = r.Name, ParentName = r.Parent }));
        db.PolicyAssignments.AddRange(assignments.Select(a => new PolicyAssignment { Subject = a.Subject, RoleName = a.Role }));
        db.PolicyGrants.AddRange(parsedGrants);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new { roles = roles.Count, assignments = assignments.Count, grants = parsedGrants.Count });
    }
}

// --- Wire contract DTOs ---

public record DecisionRequest(string? Subject, string? Action, string? Resource);

public record DecisionResponse(string Effect);

public record AuditResponse(List<AuditEntry> Entries);

public record AuditEntry(string Subject, string Action, string Resource, string Outcome, string Timestamp);

public record PolicyRequest(
    List<PolicyRoleDto>? Roles,
    List<PolicyAssignmentDto>? Assignments,
    List<PolicyGrantDto>? Grants);

public record PolicyRoleDto(string Name, string? Parent);

public record PolicyAssignmentDto(string Subject, string Role);

public record PolicyGrantDto(string Subject, string Action, string Resource, string Effect);
