namespace Gatekeeper.Web.Authorization;

// Wire-contract DTOs for the decision API. Property names serialize to camelCase
// (the ASP.NET Core minimal-API default), which matches the single-word lowercase
// field names the callers expect: subject, action, resource, effect, etc.

/// <summary>POST /api/decisions request body.</summary>
public record DecisionRequest(string? Subject, string? Action, string? Resource);

/// <summary>POST /api/decisions response body.</summary>
public record DecisionResponse(string Effect);

/// <summary>One row in the GET /api/audit response.</summary>
public record AuditEntryDto(
    string Subject,
    string Action,
    string Resource,
    string Outcome,
    string Timestamp);

/// <summary>GET /api/audit response body.</summary>
public record AuditResponse(IReadOnlyList<AuditEntryDto> Entries);

/// <summary>PUT /api/policy request body — the whole policy, replaced in one shot.</summary>
public record PolicyDocument(
    IReadOnlyList<PolicyRoleDto>? Roles,
    IReadOnlyList<PolicyAssignmentDto>? Assignments,
    IReadOnlyList<PolicyGrantDto>? Grants);

public record PolicyRoleDto(string? Name, string? Parent);

public record PolicyAssignmentDto(string? Subject, string? Role);

public record PolicyGrantDto(string? Subject, string? Action, string? Resource, string? Effect);
