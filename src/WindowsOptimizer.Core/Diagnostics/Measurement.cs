using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WindowsOptimizer.Core;

/// <summary>
/// What a snapshot can honestly measure: the background load and boot time that tweaks like startup, services and
/// telemetry change. It does not measure frame rate; for that, record a game with a frame-time tool.
/// </summary>
public sealed record SystemSnapshot(
    DateTimeOffset TakenAt,
    string Label,
    double IdleCpuPercent,
    long MemoryUsedMb,
    int Processes,
    int StartupItemsEnabled,
    int? BootSeconds,
    string PowerPlan);

public enum Verdict { Same, Better, Worse }

public sealed record ComparisonRow(string Metric, string Before, string After, string Change, Verdict Verdict);

public static class Measurement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    /// <summary>
    /// Takes a snapshot. The CPU figure is the average over <paramref name="sample"/>, so leave the PC alone while it runs
    /// (and close games and browsers) for numbers that can be compared later.
    /// </summary>
    public static async Task<SystemSnapshot> CaptureAsync(
        TweakContext ctx, string label, TimeSpan sample, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        GetSystemTimes(out var idle0, out var kernel0, out var user0);
        var steps = 20;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(sample / steps, cancel);
            progress?.Report(i / (double)steps);
        }
        GetSystemTimes(out var idle1, out var kernel1, out var user1);

        // Kernel time includes idle time, so busy = (kernel - idle) + user.
        var total = (kernel1.Value - kernel0.Value) + (user1.Value - user0.Value);
        var idle = idle1.Value - idle0.Value;
        var cpu = total == 0 ? 0 : (1 - (double)idle / total) * 100;

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);

        var processes = Process.GetProcesses();
        var processCount = processes.Length;
        foreach (var p in processes) p.Dispose();

        var startup = StartupDiscovery.Discover(ctx).Count(t => t.GetStatus() != TweakStatus.Applied);

        return new SystemSnapshot(
            DateTimeOffset.Now,
            label,
            Math.Round(Math.Max(0, cpu), 1),
            (long)((memory.TotalPhys - memory.AvailPhys) / 1024 / 1024),
            processCount,
            startup,
            ReadBootSeconds(ctx),
            ReadPowerPlan(ctx));
    }

    public static IReadOnlyList<ComparisonRow> Compare(SystemSnapshot before, SystemSnapshot after)
    {
        var rows = new List<ComparisonRow>
        {
            Row("Idle CPU use", before.IdleCpuPercent, after.IdleCpuPercent, 1.0, v => $"{v:0.#} %", "points"),
            Row("Memory in use", before.MemoryUsedMb, after.MemoryUsedMb, 100, v => $"{v:N0} MB", "MB"),
            Row("Running processes", before.Processes, after.Processes, 0, v => $"{v:N0}", ""),
            Row("Programs starting with Windows", before.StartupItemsEnabled, after.StartupItemsEnabled, 0, v => $"{v:N0}", ""),
        };

        rows.Add(before.BootSeconds is { } b && after.BootSeconds is { } a
            ? Row("Last boot time", b, a, 3, v => $"{v:N0} s", "s")
            : new ComparisonRow("Last boot time", Seconds(before.BootSeconds), Seconds(after.BootSeconds), "—", Verdict.Same));

        rows.Add(new ComparisonRow("Power plan", before.PowerPlan, after.PowerPlan,
            before.PowerPlan == after.PowerPlan ? "same" : "changed", Verdict.Same));
        return rows;
    }

    // Lower is better for every number above. Differences under `noise` are measurement jitter, not a result.
    private static ComparisonRow Row(string metric, double before, double after, double noise, Func<double, string> format, string unit)
    {
        var delta = after - before;
        var verdict = Math.Abs(delta) <= noise ? Verdict.Same : delta < 0 ? Verdict.Better : Verdict.Worse;
        var change = verdict == Verdict.Same
            ? "no real change"
            : ((delta > 0 ? "+" : "−") + Math.Abs(delta).ToString("0.#", CultureInfo.InvariantCulture) + " " + unit).TrimEnd();
        return new ComparisonRow(metric, format(before), format(after), change, verdict);
    }

    private static string Seconds(int? value) => value is { } s ? $"{s} s" : "not available";

    private static int? ReadBootSeconds(TweakContext ctx)
    {
        // Event 100 of the Diagnostics-Performance log records how long the last boot took, in milliseconds.
        var result = ctx.Runner.RunPowerShell("""
            $e = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-Diagnostics-Performance/Operational'; Id = 100 } -MaxEvents 1 -ErrorAction SilentlyContinue
            if ($e) { ([xml]$e.ToXml()).Event.EventData.Data | Where-Object { $_.Name -eq 'BootTime' } | ForEach-Object { $_.'#text' } }
            """);
        return result.Success && long.TryParse(result.Output.Trim(), CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? (int)Math.Round(ms / 1000.0)
            : null;
    }

    private static string ReadPowerPlan(TweakContext ctx)
    {
        var output = ctx.Runner.Run("powercfg", "/getactivescheme").Output;
        var open = output.LastIndexOf('(');
        var close = output.LastIndexOf(')');
        return open >= 0 && close > open ? output[(open + 1)..close] : "unknown";
    }
}

/// <summary>The snapshots the user took, kept in <c>measurements.json</c>.</summary>
public sealed class SnapshotStore
{
    private readonly string _file;

    public SnapshotStore(string dataRoot) => _file = Path.Combine(dataRoot, "measurements.json");

    public IReadOnlyList<SystemSnapshot> All()
    {
        try
        {
            return File.Exists(_file)
                ? JsonSerializer.Deserialize<List<SystemSnapshot>>(File.ReadAllText(_file)) ?? new List<SystemSnapshot>()
                : new List<SystemSnapshot>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<SystemSnapshot>();
        }
    }

    public void Add(SystemSnapshot snapshot) => Save(All().Append(snapshot).ToList());

    public void Remove(SystemSnapshot snapshot) =>
        Save(All().Where(s => s.TakenAt != snapshot.TakenAt).ToList());

    private void Save(List<SystemSnapshot> snapshots)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
    }
}
