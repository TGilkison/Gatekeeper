namespace Gatekeeper.Web.Api;

/// <summary>
/// Wire contracts for the decision API. Property names serialize to camelCase
/// (ASP.NET Core's web defaults), which matches the single-word JSON keys the
/// clients are built against: "subject", "action", "resource", "effect", etc.
/// Effect/outcome are carried as strings so the wire values are exactly
/// "Allow" / "Deny" rather than enum numbers.
/// </summary>

// --- POST /api/decisions ---

public record DecisionRequest(string Subject, string Action, string Resource);

public record DecisionResponse(string Effect);

// --- GET /api/audit ---

public record AuditResponse(IReadOnlyList<AuditEntry> Entries);

public record AuditEntry(
    string Subject,
    string Action,
    string Resource,
    string Outcome,
    string Timestamp);

// --- PUT /api/policy ---

public record PolicyDocument(
    IReadOnlyList<PolicyRole> Roles,
    IReadOnlyList<PolicyAssignment> Assignments,
    IReadOnlyList<PolicyGrant> Grants);

/// <summary><c>Parent</c> is null when the role has no parent.</summary>
public record PolicyRole(string Name, string? Parent);

public record PolicyAssignment(string Subject, string Role);

/// <summary><c>Subject</c> is either a user id or a role name.</summary>
public record PolicyGrant(string Subject, string Action, string Resource, string Effect);
