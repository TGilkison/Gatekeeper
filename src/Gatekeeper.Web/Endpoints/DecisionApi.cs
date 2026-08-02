using System.Globalization;
using Gatekeeper.Web.Services;

namespace Gatekeeper.Web.Endpoints;

/// <summary>The HTTP surface other apps call: decide, read the decision audit, replace the policy.</summary>
public static class DecisionApi
{
    /// <summary>Format a timestamp as the callers expect: UTC, second precision, trailing 'Z'.</summary>
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public static IEndpointRouteBuilder MapDecisionApi(this IEndpointRouteBuilder app)
    {
        // JSON bodies, no browser form posts: opt these endpoints out of antiforgery.
        var api = app.MapGroup("/api").DisableAntiforgery();

        api.MapPost("/decisions", async (DecisionRequest request, IPolicyService policy) =>
        {
            if (request is null
                || string.IsNullOrWhiteSpace(request.Subject)
                || string.IsNullOrWhiteSpace(request.Action)
                || string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await policy.DecideAsync(request.Subject, request.Action, request.Resource);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        });

        api.MapGet("/audit", async (string? subject, string? resource, IPolicyService policy) =>
        {
            var entries = await policy.GetAuditAsync(subject, resource);
            var dto = entries
                .Select(e => new AuditEntryDto(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome.ToString(),
                    e.Timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)))
                .ToList();

            return Results.Ok(new AuditListResponse(dto));
        });

        api.MapPut("/policy", async (PolicyDto policy, IPolicyService service) =>
        {
            try
            {
                await service.ReplaceAsync(policy);
                return Results.NoContent();
            }
            catch (PolicyValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
