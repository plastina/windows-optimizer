using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App;

public sealed record ReleaseInfo(string Version, string Url);

/// <summary>
/// Asks GitHub for the latest published release. It sends one anonymous GET request and nothing else,
/// and only when the user has left "Check for updates on start" on.
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/plastina/windows-optimizer/releases";
    private const string LatestApi = "https://api.github.com/repos/plastina/windows-optimizer/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return (info ?? "0.0.0").Split('+')[0];
        }
    }

    /// <summary>Returns the newer release, or null when up to date or when anything goes wrong (offline, rate limit...).</summary>
    public static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"WindowsOptimizer/{CurrentVersion}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            await using var stream = await http.GetStreamAsync(LatestApi);
            using var document = await JsonDocument.ParseAsync(stream);

            var tag = document.RootElement.GetProperty("tag_name").GetString();
            if (!UpdateCheck.IsNewer(CurrentVersion, tag)) return null;

            // Only ever open a page on this project's own GitHub, whatever the response says.
            var url = document.RootElement.TryGetProperty("html_url", out var html) ? html.GetString() : null;
            if (url is null || !url.StartsWith(ReleasesPage, StringComparison.OrdinalIgnoreCase)) url = ReleasesPage;

            return new ReleaseInfo(tag!.TrimStart('v', 'V'), url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
