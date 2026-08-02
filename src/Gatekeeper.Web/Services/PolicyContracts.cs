namespace Gatekeeper.Web.Services;

// Wire contracts for the decision API. Property names serialize to camelCase, which
// matches the JSON the clients are built against (subject, action, resource, effect…).

/// <summary>POST /api/decisions request body.</summary>
public record DecisionRequest(string? Subject, string? Action, string? Resource);

/// <summary>POST /api/decisions response body. <c>Effect</c> is "Allow" or "Deny".</summary>
public record DecisionResponse(string Effect);

/// <summary>GET /api/audit response body. Entries are oldest first.</summary>
public record AuditResponse(IReadOnlyList<AuditEntry> Entries);

/// <summary>A single audit entry as it appears on the wire.</summary>
public record AuditEntry(string Subject, string Action, string Resource, string Outcome, string Timestamp);

/// <summary>PUT /api/policy request body. Replaces the whole policy in one shot.</summary>
public record PolicyRequest(
    IReadOnlyList<PolicyRole>? Roles,
    IReadOnlyList<PolicyAssignment>? Assignments,
    IReadOnlyList<PolicyGrant>? Grants);

/// <summary>A role and its optional parent (null parent means no parent).</summary>
public record PolicyRole(string Name, string? Parent);

/// <summary>Assigns a role to a user.</summary>
public record PolicyAssignment(string Subject, string Role);

/// <summary>A grant. <c>Subject</c> is either a user id or a role name; <c>Effect</c> is "Allow" or "Deny".</summary>
public record PolicyGrant(string Subject, string Action, string Resource, string Effect);
