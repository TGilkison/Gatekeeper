using System.Text.Json;

namespace Gatekeeper.Web;

/// <summary>
/// Logs a loud warning on boot naming the tag and the defects live in it (ADR-0051).
///
/// This is book-facing scaffolding, not part of the running example's fiction. It is
/// the one piece of this application written by a human rather than generated, and it
/// exists because shipping a public, MIT-licensed authorization service with silent
/// audit-integrity defects and no sign on it would be reckless.
///
/// It is a feature, not a nag: it ties the running process back to the defect registry,
/// and a reader who sees "3 defects active" in their console has learned something the
/// README could not teach them.
/// </summary>
public static class LabTargetBanner
{
    public static void Log(ILogger logger, string contentRoot)
    {
        var manifest = FindManifest(contentRoot);
        var tag = "unknown";
        var lines = new List<string>();

        if (manifest is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;
                if (root.TryGetProperty("tag", out var t)) tag = t.GetString() ?? tag;
                if (root.TryGetProperty("defects", out var defects))
                {
                    foreach (var d in defects.EnumerateArray())
                    {
                        var id = d.TryGetProperty("id", out var i) ? i.GetString() : "?";
                        var sev = d.TryGetProperty("severity", out var s) ? s.GetString() : "?";
                        var title = d.TryGetProperty("title", out var ti) ? ti.GetString() : "?";
                        lines.Add($"  [{sev}] {id}: {title}");
                    }
                }
            }
            catch (Exception ex)
            {
                lines.Add($"  (could not read defects.json: {ex.Message})");
            }
        }
        else
        {
            lines.Add("  (defects.json not found; run from a checkout of the repository)");
        }

        logger.LogWarning(
            "\n" +
            "===============================================================\n" +
            " GATEKEEPER IS A LAB TARGET. DO NOT DEPLOY IT.\n" +
            "===============================================================\n" +
            " Tag: {Tag}\n" +
            " {Count} known defect(s) live at this tag:\n" +
            "{Defects}\n" +
            " This application contains deliberate, documented defects,\n" +
            " including silent authorization and audit-integrity failures.\n" +
            " It must not run on a public server, a shared network, or any\n" +
            " environment holding real data or real users. See SECURITY.md.\n" +
            "===============================================================",
            tag, lines.Count, string.Join("\n", lines));
    }

    // The manifest lives at the repository root, which is above the content root. Walk
    // up rather than hardcoding a depth, so this survives the chapter 8 project split.
    private static string? FindManifest(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "defects.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
