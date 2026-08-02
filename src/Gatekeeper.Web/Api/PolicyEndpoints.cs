using System.Globalization;
using Gatekeeper.Web.Services;

namespace Gatekeeper.Web.Api;

/// <summary>The HTTP surface other applications call to get authorization decisions.</summary>
public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // POST /api/decisions — may this subject take this action on this resource?
        api.MapPost("/decisions", async (DecisionRequest request, IPolicyEngine engine, CancellationToken ct) =>
        {
            var effect = await engine.DecideAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        });

        // GET /api/audit?subject={subject}&resource={resource} — decisions, oldest first.
        api.MapGet("/audit", async (string? subject, string? resource, IPolicyEngine engine, CancellationToken ct) =>
        {
            var entries = await engine.ReadAuditAsync(subject, resource, ct);
            var response = new AuditResponse(entries
                .Select(e => new AuditEntry(
                    e.Subject,
                    e.Action,
                    e.Resource,
                    e.Outcome.ToString(),
                    e.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))
                .ToList());
            return Results.Ok(response);
        });

        // PUT /api/policy — replace the whole policy in one shot.
        api.MapPut("/policy", async (PolicyDocument policy, IPolicyEngine engine, CancellationToken ct) =>
        {
            await engine.ReplacePolicyAsync(policy, ct);
            return Results.NoContent();
        });

        return app;
    }
}
