namespace Gatekeeper.Web.Data;

/// <summary>
/// A record of one authorization decision: the question asked (subject/action/resource)
/// and the answer given (outcome), stamped with when it was decided. Every call to the
/// decision endpoint writes exactly one of these.
/// </summary>
public class DecisionAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Subject { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    /// <summary>The effect that was returned to the caller: Allow or Deny.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
