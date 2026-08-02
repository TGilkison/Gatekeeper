using Gatekeeper.Web.Services;

namespace Gatekeeper.Web.Api;

/// <summary>Maps the HTTP API other apps call to ask for decisions, read the audit log, and set policy.</summary>
public static class DecisionApi
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ"; // e.g. 2026-07-12T18:03:11Z

    public static IEndpointRouteBuilder MapDecisionApi(this IEndpointRouteBuilder app)
    {
        // POST /api/decisions -> { "effect": "Allow" | "Deny" }
        app.MapPost("/api/decisions", async (DecisionRequest? request, IPermissionService permissions, CancellationToken ct) =>
        {
            if (request is null
                || string.IsNullOrWhiteSpace(request.Subject)
                || string.IsNullOrWhiteSpace(request.Action)
                || string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are required." });
            }

            var effect = await permissions.DecideAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        }).DisableAntiforgery();

        // GET /api/audit?subject=&resource= -> { "entries": [ ... ] }, oldest first
        app.MapGet("/api/audit", async (string? subject, string? resource, IPermissionService permissions, CancellationToken ct) =>
        {
            var entries = await permissions.GetAuditAsync(subject, resource, ct);
            var dto = entries
                .Select(e => new AuditEntryDto(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome.ToString(),
                    e.Timestamp.ToUniversalTime().ToString(TimestampFormat)))
                .ToList();
            return Results.Ok(new AuditResponse(dto));
        });

        // PUT /api/policy -> replaces the whole policy
        app.MapPut("/api/policy", async (PolicyRequest? request, IPermissionService permissions, CancellationToken ct) =>
        {
            if (request is null)
                return Results.BadRequest(new { error = "A policy body is required." });

            try
            {
                await permissions.ReplacePolicyAsync(request, ct);
                return Results.NoContent();
            }
            catch (PolicyValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();

        return app;
    }
}
