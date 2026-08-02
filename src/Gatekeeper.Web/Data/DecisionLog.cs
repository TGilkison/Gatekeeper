namespace Gatekeeper.Web.Data;

/// <summary>
/// A record of a single authorization decision: the question that was asked
/// (subject/action/resource) and the answer that was returned (<see cref="Outcome"/>).
/// One row is written for every call to the decision endpoint, capturing the effect
/// that was actually returned to the caller — this is the log of record.
/// </summary>
public class DecisionLog
{
    /// <summary>Store-generated, monotonically increasing. Ordering by this yields insertion order.</summary>
    public long Id { get; set; }

    /// <summary>The user id the decision was asked about.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action being checked, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action was checked against, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>The effect that was returned to the caller: Allow or Deny.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
