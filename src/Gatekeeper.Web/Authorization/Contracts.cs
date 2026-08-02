namespace Gatekeeper.Web.Authorization;

// Wire contract DTOs for the decision API. Property names serialize to the exact JSON the callers
// expect (System.Text.Json's camelCase web defaults line these up: Subject -> "subject", etc.).
// Effects are carried as plain strings ("Allow"/"Deny") so the JSON matches the contract regardless
// of how the GrantEffect enum happens to serialize.

/// <summary>Body of <c>POST /api/decisions</c>.</summary>
public record DecisionRequest(string? Subject, string? Action, string? Resource);

/// <summary>Response of <c>POST /api/decisions</c>.</summary>
public record DecisionResponse(string Effect);

/// <summary>Response of <c>GET /api/audit</c>.</summary>
public record AuditResponse(IReadOnlyList<AuditEntry> Entries);

/// <summary>One row of the decision audit log.</summary>
public record AuditEntry(string Subject, string Action, string Resource, string Outcome, string Timestamp);

/// <summary>Body of <c>PUT /api/policy</c>: the complete policy, which replaces whatever was stored.</summary>
public record PolicyDocument(
    IReadOnlyList<RoleDto>? Roles,
    IReadOnlyList<AssignmentDto>? Assignments,
    IReadOnlyList<GrantDto>? Grants);

/// <summary>A role and its optional parent (<c>null</c> means no parent).</summary>
public record RoleDto(string Name, string? Parent);

/// <summary>Assignment of a role to a subject (user id).</summary>
public record AssignmentDto(string Subject, string Role);

/// <summary>A grant whose subject is a user id or a role name.</summary>
public record GrantDto(string Subject, string Action, string Resource, string Effect);
