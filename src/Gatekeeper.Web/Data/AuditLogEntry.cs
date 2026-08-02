namespace Gatekeeper.Web.Data;

/// <summary>A record of a change made through the admin console: who, what, when.</summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Identity user id of the console operator who made the change (null for system actions).</summary>
    public string? ActorUserId { get; set; }

    /// <summary>Display name / email captured at the time, so the log survives user deletion.</summary>
    public string ActorName { get; set; } = "system";

    /// <summary>Created, Updated, Deleted, etc.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The entity type affected, e.g. "Role", "Grant", "User".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The affected entity's identifier.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Human-readable description of the change.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Optional tenant scope for the change.</summary>
    public Guid? CustomerId { get; set; }
}
