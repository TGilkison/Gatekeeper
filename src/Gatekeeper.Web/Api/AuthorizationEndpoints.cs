using System.Globalization;
using Gatekeeper.Web.Data;
using Gatekeeper.Web.Services.Authorization;

namespace Gatekeeper.Web.Api;

/// <summary>Maps the authorization HTTP API that other apps call over the wire.</summary>
public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationApi(this IEndpointRouteBuilder app)
    {
        // POST /api/decisions — the question the whole service exists to answer.
        app.MapPost("/api/decisions", async (DecisionRequest request, IPermissionService service, CancellationToken ct) =>
        {
            if (IsBlank(request.Subject) || IsBlank(request.Action) || IsBlank(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await service.DecideAsync(request.Subject!, request.Action!, request.Resource!, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        });

        // GET /api/audit?subject={subject}&resource={resource} — the decision log, oldest first.
        app.MapGet("/api/audit", async (string? subject, string? resource, IPermissionService service, CancellationToken ct) =>
        {
            if (IsBlank(subject) || IsBlank(resource))
            {
                return Results.BadRequest(new { error = "subject and resource query parameters are required." });
            }

            var entries = await service.GetAuditAsync(subject!, resource!, ct);
            var dtos = entries
                .Select(e => new AuditEntryDto(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome.ToString(),
                    FormatTimestamp(e.Timestamp)))
                .ToList();

            return Results.Ok(new AuditResponse(dtos));
        });

        // PUT /api/policy — replace the whole policy in one shot.
        app.MapPut("/api/policy", async (PolicyDocument document, IPermissionService service, CancellationToken ct) =>
        {
            if (!TryBuildPolicy(document, out var roles, out var assignments, out var grants, out var error))
            {
                return Results.BadRequest(new { error });
            }

            await service.ReplacePolicyAsync(roles, assignments, grants, ct);
            return Results.NoContent();
        });

        return app;
    }

    private static bool TryBuildPolicy(
        PolicyDocument document,
        out List<PolicyRole> roles,
        out List<PolicyAssignment> assignments,
        out List<PolicyGrant> grants,
        out string? error)
    {
        roles = [];
        assignments = [];
        grants = [];
        error = null;

        foreach (var role in document.Roles ?? [])
        {
            if (IsBlank(role.Name))
            {
                error = "every role needs a name.";
                return false;
            }

            // "parent: null" means no parent; an empty string is treated the same.
            roles.Add(new PolicyRole
            {
                Name = role.Name!,
                ParentName = IsBlank(role.Parent) ? null : role.Parent,
            });
        }

        foreach (var assignment in document.Assignments ?? [])
        {
            if (IsBlank(assignment.Subject) || IsBlank(assignment.Role))
            {
                error = "every assignment needs a subject and a role.";
                return false;
            }

            assignments.Add(new PolicyAssignment
            {
                Subject = assignment.Subject!,
                RoleName = assignment.Role!,
            });
        }

        foreach (var grant in document.Grants ?? [])
        {
            if (IsBlank(grant.Subject) || IsBlank(grant.Action) || IsBlank(grant.Resource))
            {
                error = "every grant needs a subject, an action and a resource.";
                return false;
            }

            if (!TryParseEffect(grant.Effect, out var effect))
            {
                error = $"grant effect must be \"Allow\" or \"Deny\", got \"{grant.Effect}\".";
                return false;
            }

            grants.Add(new PolicyGrant
            {
                Subject = grant.Subject!,
                Action = grant.Action!,
                Resource = grant.Resource!,
                Effect = effect,
            });
        }

        return true;
    }

    private static bool TryParseEffect(string? value, out GrantEffect effect)
    {
        effect = default;
        return !IsBlank(value)
            && Enum.TryParse(value, ignoreCase: true, out effect)
            && Enum.IsDefined(effect);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
