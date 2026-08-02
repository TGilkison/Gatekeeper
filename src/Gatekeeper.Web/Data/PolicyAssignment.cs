namespace Gatekeeper.Web.Data;

/// <summary>Assigns a user subject to a <see cref="PolicyRole"/> by name, over the decision API.</summary>
public class PolicyAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user id the role is assigned to, e.g. "user-42".</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The name of the role being assigned.</summary>
    public string RoleName { get; set; } = string.Empty;
}
