using Gatekeeper.Web.Data;

namespace Gatekeeper.Web.Authorization;

/// <summary>Maps the decision API other services call over HTTP.</summary>
public static class AuthorizationEndpoints
{
    public static void MapAuthorizationApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // POST /api/decisions — the permission check.
        api.MapPost("/decisions", async (DecisionRequest request, IPermissionService permissions, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject)
                || string.IsNullOrWhiteSpace(request.Action)
                || string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await permissions.DecideAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        })
        .AllowAnonymous()
        .DisableAntiforgery();

        // GET /api/audit?subject={subject}&resource={resource}
        api.MapGet("/audit", async (string? subject, string? resource, IPermissionService permissions, CancellationToken ct) =>
        {
            var entries = await permissions.GetAuditAsync(subject, resource, ct);
            return Results.Ok(new AuditResponse(entries));
        })
        .AllowAnonymous();

        // PUT /api/policy — replace the whole policy in one shot.
        api.MapPut("/policy", async (PolicyDocument policy, IPermissionService permissions, CancellationToken ct) =>
        {
            try
            {
                await permissions.ReplacePolicyAsync(policy, ct);
                return Results.NoContent();
            }
            catch (PolicyValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .AllowAnonymous()
        .DisableAntiforgery();
    }
}
