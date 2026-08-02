namespace Gatekeeper.Web.Api;

// Wire contracts for the HTTP API other apps call. Field names serialize to the exact
// lowercase JSON the clients are already built against (System.Text.Json web defaults
// lower-case the first letter, and every field here is a single lowercase word).

/// <summary>Body of POST /api/decisions.</summary>
public sealed record DecisionRequest(string? Subject, string? Action, string? Resource);

/// <summary>Response of POST /api/decisions. <see cref="Effect"/> is "Allow" or "Deny".</summary>
public sealed record DecisionResponse(string Effect);

/// <summary>One row of GET /api/audit.</summary>
public sealed record AuditEntryDto(
    string Subject,
    string Action,
    string Resource,
    string Outcome,
    string Timestamp);

/// <summary>Response of GET /api/audit, oldest entry first.</summary>
public sealed record AuditResponse(IReadOnlyList<AuditEntryDto> Entries);

/// <summary>Body of PUT /api/policy. Replaces the whole policy in one shot.</summary>
public sealed record PolicyRequest(
    List<PolicyRoleDto>? Roles,
    List<PolicyAssignmentDto>? Assignments,
    List<PolicyGrantDto>? Grants);

/// <summary>A role and, optionally, the parent role it inherits from (<c>null</c> = no parent).</summary>
public sealed record PolicyRoleDto(string? Name, string? Parent);

/// <summary>Assigns a role (by name) to a subject (a user id).</summary>
public sealed record PolicyAssignmentDto(string? Subject, string? Role);

/// <summary>An allow/deny grant. <see cref="Subject"/> is a user id or a role name; <see cref="Effect"/> is "Allow" or "Deny".</summary>
public sealed record PolicyGrantDto(string? Subject, string? Action, string? Resource, string? Effect);
