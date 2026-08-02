using Gatekeeper.Web.Data;
using Gatekeeper.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Api;

/// <summary>
/// The HTTP surface other apps call: ask for a decision, read the decision audit log, and replace
/// the whole policy. The wire shapes here are a fixed contract — clients are already built to them.
/// </summary>
public static class DecisionEndpoints
{
    public static IEndpointRouteBuilder MapDecisionApi(this IEndpointRouteBuilder app)
    {
        // POST /api/decisions — answer whether a subject may take an action on a resource.
        app.MapPost("/api/decisions", async (
            DecisionRequest request,
            IDecisionService decisions,
            CancellationToken ct) =>
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.Subject) ||
                string.IsNullOrWhiteSpace(request.Action) ||
                string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await decisions.DecideAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect));
        })
        .DisableAntiforgery();

        // GET /api/audit?subject=&resource= — decisions for a subject/resource, oldest first.
        app.MapGet("/api/audit", async (
            string? subject,
            string? resource,
            IDbContextFactory<GatekeeperDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var query = db.DecisionAudit.AsQueryable();
            if (!string.IsNullOrEmpty(subject))
            {
                query = query.Where(e => e.Subject == subject);
            }
            if (!string.IsNullOrEmpty(resource))
            {
                query = query.Where(e => e.Resource == resource);
            }

            var entries = await query
                .OrderBy(e => e.Timestamp)
                .Select(e => new AuditEntry(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome,
                    e.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")))
                .ToListAsync(ct);

            return Results.Ok(new AuditResponse(entries));
        });

        // PUT /api/policy — replace the entire policy (roles, assignments, grants) in one shot.
        app.MapPut("/api/policy", async (
            PolicyDocument policy,
            IDbContextFactory<GatekeeperDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Replace wholesale: clear what's there, then write the new document.
            db.PolicyRoles.RemoveRange(db.PolicyRoles);
            db.PolicyAssignments.RemoveRange(db.PolicyAssignments);
            db.PolicyGrants.RemoveRange(db.PolicyGrants);
            await db.SaveChangesAsync(ct);

            foreach (var role in policy.Roles ?? [])
            {
                db.PolicyRoles.Add(new PolicyRole { Name = role.Name, ParentName = role.Parent });
            }
            foreach (var assignment in policy.Assignments ?? [])
            {
                db.PolicyAssignments.Add(new PolicyAssignment { Subject = assignment.Subject, RoleName = assignment.Role });
            }
            foreach (var grant in policy.Grants ?? [])
            {
                db.PolicyGrants.Add(new PolicyGrant
                {
                    Subject = grant.Subject,
                    Action = grant.Action,
                    Resource = grant.Resource,
                    Effect = grant.Effect,
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.NoContent();
        })
        .DisableAntiforgery();

        return app;
    }
}

// --- Wire contract DTOs. Property names serialize to camelCase; GrantEffect serializes as "Allow"/"Deny". ---

public record DecisionRequest(string Subject, string Action, string Resource);

public record DecisionResponse(GrantEffect Effect);

public record AuditResponse(IReadOnlyList<AuditEntry> Entries);

public record AuditEntry(string Subject, string Action, string Resource, GrantEffect Outcome, string Timestamp);

public record PolicyDocument(
    IReadOnlyList<PolicyRoleDto>? Roles,
    IReadOnlyList<PolicyAssignmentDto>? Assignments,
    IReadOnlyList<PolicyGrantDto>? Grants);

public record PolicyRoleDto(string Name, string? Parent);

public record PolicyAssignmentDto(string Subject, string Role);

public record PolicyGrantDto(string Subject, string Action, string Resource, GrantEffect Effect);
