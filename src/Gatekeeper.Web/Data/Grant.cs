namespace Gatekeeper.Web.Data;

/// <summary>
/// An allow/deny of a <see cref="Permission"/> on a <see cref="Resource"/>. A grant hangs off
/// exactly one subject: either a user (<see cref="UserId"/>) or a role (<see cref="RoleId"/>).
/// </summary>
public class Grant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public GrantEffect Effect { get; set; } = GrantEffect.Allow;

    /// <summary>The action being controlled, e.g. "billing.read" or "documents.delete".</summary>
    public string Permission { get; set; } = string.Empty;

    /// <summary>The thing the permission applies to, e.g. "*", "project:42", "invoice:*".</summary>
    public string Resource { get; set; } = "*";

    // --- Subject: exactly one of the two below is set ---

    public string? UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public Guid? RoleId { get; set; }

    public Role? Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Convenience: which kind of subject this grant is attached to.</summary>
    public GrantSubjectType SubjectType => RoleId is not null ? GrantSubjectType.Role : GrantSubjectType.User;
}
