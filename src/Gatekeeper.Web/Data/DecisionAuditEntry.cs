namespace Gatekeeper.Web.Data;

/// <summary>
/// An append-only record of a single authorization decision produced by the decision
/// endpoint: which subject asked to take which action on which resource, and what the
/// engine answered. This is distinct from <see cref="AuditLogEntry"/>, which records
/// changes made through the admin console.
/// </summary>
public class DecisionAuditEntry
{
    /// <summary>Monotonic identity key. Doubles as a stable tie-breaker when two decisions
    /// land in the same clock second, so "oldest first" ordering is deterministic.</summary>
    public long Id { get; set; }

    /// <summary>The subject the decision was asked about (a user id).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action that was requested, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action was requested against, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>The effect that was actually returned to the caller for this decision.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
