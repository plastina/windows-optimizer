using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Velopack;
using Velopack.Sources;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App;

/// <summary>A newer release. <paramref name="Downloaded"/> means it is already on disk and only needs a restart.</summary>
public sealed record ReleaseInfo(string Version, string Url, bool Downloaded = false);

/// <summary>
/// Looks for a newer release on GitHub, only when the user has left "Check for updates on start" on.
/// Installed copies (Setup.exe or the portable zip) download it in the background and install it when the
/// app closes. Anything else (a development build, the old standalone exe) can only point to the releases page.
/// </summary>
public static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/plastina/windows-optimizer";
    public const string ReleasesPage = RepositoryUrl + "/releases";
    private const string LatestApi = "https://api.github.com/repos/plastina/windows-optimizer/releases/latest";

    private static UpdateManager? _manager;
    private static UpdateInfo? _pending;

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
        var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        return manager.IsInstalled ? await DownloadAsync(manager) : await AskGitHubAsync();
    }

    /// <summary>Closes the app, installs the downloaded update and opens the new version.</summary>
    public static void RestartToUpdate()
    {
        if (_manager is not null && _pending is not null) _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    private static async Task<ReleaseInfo?> DownloadAsync(UpdateManager manager)
    {
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return null;

            await manager.DownloadUpdatesAsync(update);
            // If the user never clicks "Restart now", the update is installed quietly when the app closes.
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);

            _manager = manager;
            _pending = update;
            return new ReleaseInfo(update.TargetFullRelease.Version.ToString(), ReleasesPage, Downloaded: true);
        }
        catch (Exception)
        {
            // Velopack reports network, disk and GitHub errors with many exception types. An update that fails
            // is tried again on the next start; it must never take the app down.
            return null;
        }
    }

    private static async Task<ReleaseInfo?> AskGitHubAsync()
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
