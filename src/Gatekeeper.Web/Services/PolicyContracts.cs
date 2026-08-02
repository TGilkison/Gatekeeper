namespace Gatekeeper.Web.Services;

// Wire contract DTOs for the HTTP API. Property names serialize to camelCase under the
// default ASP.NET Core JSON options, matching what the already-built clients expect.

/// <summary>POST /api/decisions request body.</summary>
public sealed record DecisionRequest(string? Subject, string? Action, string? Resource);

/// <summary>POST /api/decisions response body. Effect is "Allow" or "Deny".</summary>
public sealed record DecisionResponse(string Effect);

/// <summary>GET /api/audit response body.</summary>
public sealed record AuditResponse(IReadOnlyList<AuditEntryDto> Entries);

/// <summary>One row of the decision audit log, oldest first in the response.</summary>
public sealed record AuditEntryDto(
    string Subject,
    string Action,
    string Resource,
    string Outcome,
    string Timestamp);

/// <summary>PUT /api/policy request body. Replaces the whole policy for the API tenant.</summary>
public sealed record PolicyDto(
    List<PolicyRoleDto>? Roles,
    List<PolicyAssignmentDto>? Assignments,
    List<PolicyGrantDto>? Grants);

/// <summary>A role and its optional parent. <c>Parent</c> is null when the role has no parent.</summary>
public sealed record PolicyRoleDto(string? Name, string? Parent);

/// <summary>Assigns a role (by name) to a subject (a user id).</summary>
public sealed record PolicyAssignmentDto(string? Subject, string? Role);

/// <summary>A grant. <c>Subject</c> is either a user id or a role name; <c>Effect</c> is "Allow" or "Deny".</summary>
public sealed record PolicyGrantDto(string? Subject, string? Action, string? Resource, string? Effect);

/// <summary>Thrown when a submitted policy is malformed; surfaced to callers as HTTP 400.</summary>
public sealed class PolicyValidationException(string message) : Exception(message);
