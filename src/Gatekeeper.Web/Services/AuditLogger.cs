using System.Security.Claims;
using Gatekeeper.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>Writes audit entries, capturing the current console operator automatically.</summary>
public interface IAuditLogger
{
    Task LogAsync(string action, string entityType, string entityId, string summary, Guid? customerId = null);
}

public class AuditLogger(
    IDbContextFactory<GatekeeperDbContext> dbFactory,
    AuthenticationStateProvider authState) : IAuditLogger
{
    public async Task LogAsync(string action, string entityType, string entityId, string summary, Guid? customerId = null)
    {
        var state = await authState.GetAuthenticationStateAsync();
        var user = state.User;

        await using var db = await dbFactory.CreateDbContextAsync();
        db.AuditLog.Add(new AuditLogEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            CustomerId = customerId,
            ActorUserId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            ActorName = user.Identity?.Name ?? "system",
        });
        await db.SaveChangesAsync();
    }
}
