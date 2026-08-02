using Gatekeeper.Web.Services;

namespace Gatekeeper.Web.Endpoints;

/// <summary>
/// The HTTP surface other apps call: ask for a decision, read the audit log, replace the policy.
/// These are plain JSON endpoints for service-to-service calls, so they sit outside the
/// cookie-authenticated Blazor console and outside antiforgery.
/// </summary>
public static class PolicyApi
{
    public static IEndpointRouteBuilder MapPolicyApi(this IEndpointRouteBuilder app)
    {
        // POST /api/decisions — can this subject do this action to this resource?
        app.MapPost("/api/decisions", async (DecisionRequest request, IPolicyService policy, CancellationToken ct) =>
        {
            if (request is null
                || string.IsNullOrWhiteSpace(request.Subject)
                || string.IsNullOrWhiteSpace(request.Action)
                || string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await policy.DecideAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        })
        .DisableAntiforgery();

        // GET /api/audit?subject={subject}&resource={resource} — recorded decisions, oldest first.
        app.MapGet("/api/audit", async (string? subject, string? resource, IPolicyService policy, CancellationToken ct) =>
        {
            var entries = await policy.GetAuditAsync(subject, resource, ct);
            var response = new AuditResponse(entries
                .Select(e => new AuditEntry(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome.ToString(),
                    PolicyService.FormatTimestamp(e.Timestamp)))
                .ToList());
            return Results.Ok(response);
        });

        // PUT /api/policy — replace the whole policy in one shot.
        app.MapPut("/api/policy", async (PolicyRequest policyBody, IPolicyService policy, CancellationToken ct) =>
        {
            if (policyBody is null)
            {
                return Results.BadRequest(new { error = "A policy body is required." });
            }
            await policy.ReplacePolicyAsync(policyBody, ct);
            return Results.NoContent();
        })
        .DisableAntiforgery();

        return app;
    }
}
