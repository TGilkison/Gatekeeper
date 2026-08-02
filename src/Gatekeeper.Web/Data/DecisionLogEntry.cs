namespace Gatekeeper.Web.Data;

/// <summary>
/// An append-only record of a single authorization decision: the question the caller
/// asked (subject/action/resource) and the answer Gatekeeper actually returned.
/// This is distinct from <see cref="AuditLogEntry"/>, which records console changes.
/// </summary>
public class DecisionLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The subject (user id) the decision was made for.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The action that was requested, e.g. "document:delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The resource the action was requested on, e.g. "doc-7".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>The effect Gatekeeper returned. This must always match what the caller was told.</summary>
    public GrantEffect Outcome { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
