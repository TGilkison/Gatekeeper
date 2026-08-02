namespace Gatekeeper.Web.Data;

/// <summary>Assigns a <see cref="PolicyRole"/> to a subject (a user id) in the decision policy.</summary>
public class PolicyAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The subject the role is assigned to, e.g. "user-42".</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The name of the assigned role, e.g. "Editor".</summary>
    public string RoleName { get; set; } = string.Empty;
}
