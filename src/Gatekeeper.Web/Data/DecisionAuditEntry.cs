namespace Gatekeeper.Web.Data;

/// <summary>
/// An append-only record of one authorization decision served over the HTTP API:
/// which subject asked to take which action on which resource, and what Gatekeeper
/// answered. Distinct from <see cref="AuditLogEntry"/>, which records console changes.
/// </summary>
public class DecisionAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The tenant the decision was evaluated under.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>The subject (caller's user id) the decision was made for.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action that was checked, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action was checked against, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>The decision Gatekeeper actually returned. Recorded faithfully.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
