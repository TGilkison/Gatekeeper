namespace Gatekeeper.Web.Data;

/// <summary>Assignment of a <see cref="PolicyRole"/> to a subject (user id) in the HTTP API policy.</summary>
public class PolicyAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user id the role is assigned to, e.g. "user-42".</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The name of the role assigned.</summary>
    public string RoleName { get; set; } = string.Empty;
}
