namespace WindowsOptimizer.Core;

/// <summary>Compares the running version with the latest release tag (for example "v0.2.0").</summary>
public static class UpdateCheck
{
    /// <summary>Parses "v1.2.3", "1.2.3+abc123" or "1.2.3-rc1" into 1.2.3. Pre-release and build suffixes are ignored.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var core = text.Trim().TrimStart('v', 'V');
        var end = core.IndexOfAny(new[] { '-', '+' });
        if (end >= 0) core = core[..end];

        if (!Version.TryParse(core, out var parsed)) return false;

        // Version.TryParse("1.2") has Build = -1; normalise so 1.2 equals 1.2.0.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    /// <summary>True when <paramref name="latestTag"/> is a higher version than <paramref name="current"/>. Unparseable input is never "newer".</summary>
    public static bool IsNewer(string? current, string? latestTag) =>
        TryParseVersion(current, out var running)
        && TryParseVersion(latestTag, out var latest)
        && latest > running;
}
