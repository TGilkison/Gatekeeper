namespace Gatekeeper.Web.Data;

/// <summary>
/// A record of a single authorization decision made through the HTTP API: the question asked
/// (subject, action, resource), the answer given (<see cref="Outcome"/>), and when.
/// </summary>
public class DecisionAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Subject { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    /// <summary>The effect returned to the caller: Allow or Deny.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
