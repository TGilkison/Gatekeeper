namespace Gatekeeper.Web.Api;

// Wire contracts for the authorization HTTP API. Property names are single lowercase
// words, which the default camelCase JSON policy serializes exactly as the clients expect.

/// <summary>POST /api/decisions request body.</summary>
public sealed record DecisionRequest(string? Subject, string? Action, string? Resource);

/// <summary>POST /api/decisions response body.</summary>
public sealed record DecisionResponse(string Effect);

/// <summary>GET /api/audit response body.</summary>
public sealed record AuditResponse(IReadOnlyList<AuditEntryDto> Entries);

/// <summary>One entry in the decision audit log.</summary>
public sealed record AuditEntryDto(
    string Subject,
    string Action,
    string Resource,
    string Outcome,
    string Timestamp);

/// <summary>PUT /api/policy request body: the entire policy in one document.</summary>
public sealed record PolicyDocument(
    IReadOnlyList<PolicyRoleDto>? Roles,
    IReadOnlyList<PolicyAssignmentDto>? Assignments,
    IReadOnlyList<PolicyGrantDto>? Grants);

public sealed record PolicyRoleDto(string? Name, string? Parent);

public sealed record PolicyAssignmentDto(string? Subject, string? Role);

public sealed record PolicyGrantDto(string? Subject, string? Action, string? Resource, string? Effect);
