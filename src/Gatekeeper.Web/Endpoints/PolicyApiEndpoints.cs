using Gatekeeper.Web.Services;

namespace Gatekeeper.Web.Endpoints;

/// <summary>
/// The HTTP surface other apps call: ask for a decision, read the decision audit log,
/// and replace the policy. These are anonymous machine-to-machine endpoints, kept
/// separate from the authenticated Blazor console.
/// </summary>
public static class PolicyApiEndpoints
{
    public static void MapPolicyApiEndpoints(this WebApplication app)
    {
        // POST /api/decisions  ->  { "effect": "Allow" | "Deny" }
        app.MapPost("/api/decisions", async (DecisionRequest request, IPolicyService policy, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject)
                || string.IsNullOrWhiteSpace(request.Action)
                || string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await policy.EvaluateAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        }).DisableAntiforgery();

        // GET /api/audit?subject={subject}&resource={resource}  ->  { "entries": [ ... ] }
        app.MapGet("/api/audit", async (string? subject, string? resource, IPolicyService policy, CancellationToken ct) =>
        {
            var entries = await policy.GetAuditAsync(subject, resource, ct);
            var dtos = entries
                .Select(e => new AuditEntryDto(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome.ToString(),
                    e.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")))
                .ToList();
            return Results.Ok(new AuditResponse(dtos));
        });

        // PUT /api/policy  ->  204 No Content (replaces the whole policy)
        app.MapPut("/api/policy", async (PolicyDto policy, IPolicyService service, CancellationToken ct) =>
        {
            try
            {
                await service.ReplacePolicyAsync(policy, ct);
                return Results.NoContent();
            }
            catch (PolicyValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();
    }
}
