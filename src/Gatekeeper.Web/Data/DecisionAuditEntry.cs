namespace Gatekeeper.Web.Data;

/// <summary>
/// An append-only record of one authorization decision: the exact question asked
/// (subject/action/resource) and the exact answer given (<see cref="Outcome"/>).
/// The outcome recorded here is the same value returned to the caller — the log
/// never disagrees with the decision.
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
