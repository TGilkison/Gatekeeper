namespace Gatekeeper.Web.Data;

/// <summary>
/// A record of a single authorization decision: the exact question a caller asked
/// (<see cref="Subject"/> may <see cref="Action"/> on <see cref="Resource"/>?) and the
/// answer Gatekeeper gave. Written on every decision so the log is a faithful, replayable
/// history of what was allowed and denied.
/// </summary>
public class DecisionAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The subject the decision was made for (a user id).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action that was requested, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action was requested against, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>The decision that was returned. This is the effect the caller actually received.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
