using System.Globalization;

namespace WindowsOptimizer.Core;

/// <summary>Disk cleanups that show how much space they would free before deleting anything.</summary>
public static class CleanupTweaks
{
    // Temp files younger than this may belong to something that is running right now.
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static ITweak TempFiles(TweakContext c) => new DelegateTweak(
        c, "clean-temp-files", "Clean temporary files",
        "Deletes files older than 24 hours from your temp folder and the Windows temp folder. Files that are in use are skipped.",
        TweakCategory.Privacy, RiskLevel.Low,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var freed = TempFolders().Sum(folder => Delete(folder.Path, MinimumAge));
            return TweakResult.Ok($"Freed {FormatBytes(freed)}.");
        },
        preview: () => TempFolders()
            .Select(f =>
            {
                var (files, bytes) = Measure(f.Path, MinimumAge);
                return new PreviewLine(f.Label, $"{FormatBytes(bytes)} in {files:N0} file(s)", "removed", f.Path);
            })
            .ToList())
    {
        NeedsAdmin = true,
    };

    public static ITweak UpdateCache(TweakContext c)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");

        return new DelegateTweak(
            c, "clean-update-cache", "Clear the Windows Update download cache",
            "Deletes the update files Windows already downloaded. Windows downloads them again if an update is still waiting, so this only helps with leftovers. The update services are stopped for a moment and then restored.",
            TweakCategory.Privacy, RiskLevel.Low,
            status: () => TweakStatus.Unknown,
            apply: () =>
            {
                // Windows keeps these files open while its services run, so stop them first and put back what was running.
                var services = new[] { "wuauserv", "bits" };
                var wasRunning = services.Where(s => c.Runner.Run("sc.exe", $"query {s}").Output.Contains("RUNNING", StringComparison.Ordinal)).ToList();
                foreach (var service in wasRunning) c.Runner.Run("net.exe", $"stop {service} /y");

                long freed;
                try
                {
                    freed = Delete(folder, TimeSpan.Zero);
                }
                finally
                {
                    foreach (var service in wasRunning) c.Runner.Run("net.exe", $"start {service}");
                }

                return TweakResult.Ok($"Freed {FormatBytes(freed)}.");
            },
            preview: () =>
            {
                var (files, bytes) = Measure(folder, TimeSpan.Zero);
                return new[] { new PreviewLine("Downloaded update files", $"{FormatBytes(bytes)} in {files:N0} file(s)", "removed", folder) };
            })
        {
            NeedsAdmin = true,
        };
    }

    private static IEnumerable<(string Label, string Path)> TempFolders()
    {
        var folders = new List<(string, string)>
        {
            ("Your temp folder", Path.GetTempPath().TrimEnd('\\')),
            ("Windows temp folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")),
        };
        return folders.Where(f => Directory.Exists(f.Item2)).DistinctBy(f => f.Item2, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Counts the files (and their size) that <see cref="Delete"/> would try to remove.</summary>
    public static (int Files, long Bytes) Measure(string folder, TimeSpan olderThan)
    {
        if (!Directory.Exists(folder)) return (0, 0);

        var cutoff = DateTime.Now - olderThan;
        var files = 0;
        long bytes = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Everything))
        {
            if (file.LastWriteTime > cutoff) continue;
            files++;
            bytes += file.Length;
        }
        return (files, bytes);
    }

    /// <summary>Deletes files older than <paramref name="olderThan"/> and any folders left empty. Returns the bytes freed.</summary>
    public static long Delete(string folder, TimeSpan olderThan)
    {
        if (!Directory.Exists(folder)) return 0;

        var cutoff = DateTime.Now - olderThan;
        long freed = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Everything).ToList())
        {
            if (file.LastWriteTime > cutoff) continue;
            try
            {
                var length = file.Length;
                file.Delete();
                freed += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // Deepest folders first, so a folder emptied by the loop above can go too. Only empty ones are removed.
        foreach (var dir in new DirectoryInfo(folder).EnumerateDirectories("*", Everything).OrderByDescending(d => d.FullName.Length).ToList())
        {
            try
            {
                if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} B"
            : value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
