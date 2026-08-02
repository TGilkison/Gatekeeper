namespace Gatekeeper.Web.Data;

/// <summary>Subscription tier a customer (tenant) is on.</summary>
public enum PlanTier
{
    Free = 0,
    Pro = 1,
    Enterprise = 2,
}

/// <summary>Whether a grant allows or denies a permission.</summary>
public enum GrantEffect
{
    Allow = 0,
    Deny = 1,
}

/// <summary>The kind of subject a grant hangs off of.</summary>
public enum GrantSubjectType
{
    User = 0,
    Role = 1,
}
