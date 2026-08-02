namespace Gatekeeper.Web.Authorization;

/// <summary>Maps the HTTP decision API that other applications call: decisions, audit, and policy.</summary>
public static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationApi(this IEndpointRouteBuilder app)
    {
        // POST /api/decisions — answer "can this subject do this action to this resource?"
        app.MapPost("/api/decisions", async (DecisionRequest request, IPolicyService policy, CancellationToken ct) =>
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.Subject) ||
                string.IsNullOrWhiteSpace(request.Action) ||
                string.IsNullOrWhiteSpace(request.Resource))
            {
                return Results.BadRequest(new { error = "subject, action and resource are all required." });
            }

            var effect = await policy.DecideAsync(request.Subject, request.Action, request.Resource, ct);
            return Results.Ok(new DecisionResponse(effect.ToString()));
        });

        // GET /api/audit?subject=&resource= — decision history, oldest first.
        app.MapGet("/api/audit", async (string? subject, string? resource, IPolicyService policy, CancellationToken ct) =>
        {
            var entries = await policy.GetAuditAsync(subject, resource, ct);
            return Results.Ok(new AuditResponse(entries));
        });

        // PUT /api/policy — replace the whole policy in one shot.
        app.MapPut("/api/policy", async (PolicyDocument document, IPolicyService policy, CancellationToken ct) =>
        {
            if (document is null)
            {
                return Results.BadRequest(new { error = "A policy document is required." });
            }

            try
            {
                await policy.ReplacePolicyAsync(document, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return Results.NoContent();
        });

        return app;
    }
}
