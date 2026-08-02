using Gatekeeper.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Gatekeeper.Web.Services;

/// <summary>
/// Answers the question Gatekeeper exists to answer: may this subject take this action on this
/// resource? Resolves the subject's roles (following each role's parent chain), gathers every
/// grant that applies, and combines them with deny-overrides and a default of deny. Every call is
/// written to the decision audit log.
/// </summary>
public interface IDecisionService
{
    Task<GrantEffect> DecideAsync(string subject, string action, string resource, CancellationToken ct = default);
}

public class DecisionService(IDbContextFactory<GatekeeperDbContext> dbFactory) : IDecisionService
{
    /// <summary>Guards the role-parent walk against a cycle or a pathological chain.</summary>
    private const int MaxRoleDepth = 100;

    public async Task<GrantEffect> DecideAsync(
        string subject, string action, string resource, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Every subject the grants can hang off of for this decision: the user themselves plus
        // every role they hold, expanded up each role's parent chain.
        var subjects = await ResolveSubjectsAsync(db, subject, ct);

        // Grants that match this exact action/resource and hang off one of those subjects.
        var candidates = await db.PolicyGrants
            .Where(g => g.Action == action && g.Resource == resource)
            .Where(g => subjects.Contains(g.Subject))
            .Select(g => g.Effect)
            .ToListAsync(ct);

        // Deny-overrides: an explicit deny beats any allow; with no matching grant we default to deny.
        var effect = candidates.Contains(GrantEffect.Deny)
            ? GrantEffect.Deny
            : candidates.Contains(GrantEffect.Allow)
                ? GrantEffect.Allow
                : GrantEffect.Deny;

        db.DecisionAudit.Add(new DecisionAuditEntry
        {
            Subject = subject,
            Action = action,
            Resource = resource,
            Outcome = effect,
            Timestamp = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return effect;
    }

    /// <summary>
    /// Returns the subject plus every role it picks up, following each assigned role's parent
    /// chain. A visited set and a depth cap keep a cyclic role graph from looping forever.
    /// </summary>
    private static async Task<HashSet<string>> ResolveSubjectsAsync(
        GatekeeperDbContext db, string subject, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { subject };

        var directRoles = await db.PolicyAssignments
            .Where(a => a.Subject == subject)
            .Select(a => a.RoleName)
            .ToListAsync(ct);

        if (directRoles.Count == 0)
        {
            return result;
        }

        // name -> parent name, so we can walk the hierarchy without another round trip per role.
        var parents = await db.PolicyRoles
            .Select(r => new { r.Name, r.ParentName })
            .ToDictionaryAsync(r => r.Name, r => r.ParentName, StringComparer.Ordinal, ct);

        var pending = new Queue<string>(directRoles);
        var depth = 0;
        while (pending.Count > 0 && depth < MaxRoleDepth)
        {
            depth++;
            for (var i = pending.Count; i > 0; i--)
            {
                var role = pending.Dequeue();
                if (!result.Add(role))
                {
                    continue; // already seen — avoids cycles
                }

                if (parents.TryGetValue(role, out var parent) && !string.IsNullOrEmpty(parent))
                {
                    pending.Enqueue(parent);
                }
            }
        }

        return result;
    }
}
