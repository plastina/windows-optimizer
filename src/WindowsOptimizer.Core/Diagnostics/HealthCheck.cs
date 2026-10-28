using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

public enum HealthLevel { Good, Attention, Info }

/// <summary>One line of the health check. <see cref="Advice"/> says what to do about it, when there is something to do.</summary>
public sealed record HealthItem(string Title, HealthLevel Level, string Detail, string? Advice = null);

public readonly record struct DisplayInfo(int Width, int Height, int CurrentHz, int MaxHz);

public interface IDisplayProbe
{
    /// <summary>The primary display: its current refresh rate and the highest one it offers at the same resolution.</summary>
    DisplayInfo? Primary();
}

/// <summary>
/// Looks for the things that cost the most performance and that no registry tweak can fix: a monitor running below its
/// refresh rate, memory below its rated speed, an old graphics driver, a full or slow drive. Read-only; changes nothing.
/// </summary>
public sealed class HealthCheck
{
    private readonly TweakContext _ctx;
    private readonly IDisplayProbe _display;

    public HealthCheck(TweakContext ctx, IDisplayProbe? display = null)
    {
        _ctx = ctx;
        _display = display ?? new WindowsDisplayProbe();
    }

    public IReadOnlyList<HealthItem> Run()
    {
        var items = new List<HealthItem>();
        items.Add(CheckRefreshRate());

        var hardware = ReadHardware();
        items.Add(CheckMemorySpeed(hardware));
        items.AddRange(CheckGraphicsDrivers(hardware));
        items.Add(CheckDrives(hardware));
        items.Add(CheckFreeSpace());
        items.Add(CheckPowerPlan());
        items.Add(CheckMemoryIntegrity());
        items.Add(CheckUptime());
        return items;
    }

    private HealthItem CheckRefreshRate()
    {
        const string title = "Monitor refresh rate";
        var info = _display.Primary();
        if (info is not { } d) return new HealthItem(title, HealthLevel.Info, "Could not read the display settings.");

        var detail = $"{d.Width}×{d.Height} at {d.CurrentHz} Hz";
        if (d.MaxHz > d.CurrentHz + 5)
        {
            return new HealthItem(title, HealthLevel.Attention, $"{detail}, but the monitor can do {d.MaxHz} Hz at this resolution.",
                "Open Settings > System > Display > Advanced display and pick the highest refresh rate. Use a DisplayPort or HDMI cable that supports it.");
        }
        return new HealthItem(title, HealthLevel.Good, detail + ", the highest it offers.");
    }

    private HealthItem CheckMemorySpeed(Hardware hardware)
    {
        const string title = "Memory speed (XMP / EXPO)";
        var sticks = hardware.Ram.Where(r => r.Speed > 0 && r.Configured > 0).ToList();
        if (sticks.Count == 0) return new HealthItem(title, HealthLevel.Info, "Could not read the memory modules.");

        var rated = sticks.Max(s => s.Speed);
        var running = sticks.Min(s => s.Configured);
        var totalGb = hardware.Ram.Sum(r => r.Gb);
        var detail = $"{totalGb} GB in {hardware.Ram.Count} module(s), running at {running} MT/s; the modules are rated for {rated} MT/s.";

        // Modules usually report their rated speed in Speed and the speed they run at in ConfiguredClockSpeed.
        if (running + 100 < rated)
        {
            return new HealthItem(title, HealthLevel.Attention, detail,
                "The memory is running slower than it can. Turn on XMP (Intel) or EXPO (AMD) in the BIOS/UEFI. It is one of the biggest free performance gains on a gaming PC.");
        }
        return new HealthItem(title, HealthLevel.Good, detail);
    }

    private IEnumerable<HealthItem> CheckGraphicsDrivers(Hardware hardware)
    {
        const string title = "Graphics driver";
        if (hardware.Gpu.Count == 0)
        {
            yield return new HealthItem(title, HealthLevel.Info, "Could not read the graphics adapters.");
            yield break;
        }

        foreach (var gpu in hardware.Gpu)
        {
            if (gpu.Date is not { } date)
            {
                yield return new HealthItem($"{title}: {gpu.Name}", HealthLevel.Info, "The driver date is not reported.");
                continue;
            }

            var months = (int)((DateTime.Now - date).TotalDays / 30.4);
            var detail = $"Driver from {date:yyyy-MM-dd} ({months} months old).";
            yield return months >= 12
                ? new HealthItem($"{title}: {gpu.Name}", HealthLevel.Attention, detail,
                    "Install the latest driver from NVIDIA, AMD or Intel. New games often ship fixes for stutter and crashes in drivers.")
                : new HealthItem($"{title}: {gpu.Name}", HealthLevel.Good, detail);
        }
    }

    private static HealthItem CheckDrives(Hardware hardware)
    {
        const string title = "Drive type";
        if (hardware.Disks.Count == 0) return new HealthItem(title, HealthLevel.Info, "Could not read the physical drives.");

        var list = string.Join("; ", hardware.Disks.Select(d => $"{d.Name} ({d.Media})"));
        return hardware.Disks.Any(d => d.Media.Equals("HDD", StringComparison.OrdinalIgnoreCase))
            ? new HealthItem(title, HealthLevel.Info, list,
                "A hard disk is in the list. Install the games you play most on an SSD: loading is several times faster.")
            : new HealthItem(title, HealthLevel.Good, list);
    }

    private static HealthItem CheckFreeSpace()
    {
        const string title = "Free space on the system drive";
        try
        {
            var drive = new DriveInfo(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var percent = drive.AvailableFreeSpace * 100d / drive.TotalSize;
            var detail = string.Create(CultureInfo.InvariantCulture, $"{freeGb:0.#} GB free ({percent:0}%) on {drive.Name}.");

            return percent < 15 || freeGb < 30
                ? new HealthItem(title, HealthLevel.Attention, detail,
                    "An SSD gets slower and wears faster when it is almost full. Free some space; the \"List files of 500 MB or more\" and cleanup tweaks help.")
                : new HealthItem(title, HealthLevel.Good, detail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new HealthItem(title, HealthLevel.Info, "Could not read the drive.");
        }
    }

    private HealthItem CheckPowerPlan()
    {
        const string title = "Power plan";
        var result = _ctx.Runner.Run("powercfg", "/getactivescheme");
        var open = result.Output.LastIndexOf('(');
        var close = result.Output.LastIndexOf(')');
        if (!result.Success || open < 0 || close < open) return new HealthItem(title, HealthLevel.Info, "Could not read the active power plan.");

        var name = result.Output[(open + 1)..close];
        var fast = Regex.IsMatch(name, "Ultimate|High perf|Alto desempenho|Máxim|Maxim", RegexOptions.IgnoreCase);
        return fast
            ? new HealthItem(title, HealthLevel.Good, name)
            : new HealthItem(title, HealthLevel.Info, name,
                "A balanced plan lets the CPU slow down between bursts. On a desktop PC, the \"Ultimate Performance power plan\" tweak turns that off.");
    }

    private HealthItem CheckMemoryIntegrity()
    {
        const string title = "Memory integrity (Core isolation)";
        var on = _ctx.Registry.GetValue(RegHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled") is { Kind: RegKind.DWord, Data: 1 };

        return on
            ? new HealthItem(title, HealthLevel.Info, "On. It protects the kernel, and in some games it costs a few percent of frame rate.",
                "Leave it on unless you measured a real loss in your games. There is a tweak to turn it off, with a security warning.")
            : new HealthItem(title, HealthLevel.Info, "Off or not configured on this PC.");
    }

    private static HealthItem CheckUptime()
    {
        const string title = "Time since the last restart";
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var detail = uptime.TotalDays >= 1 ? $"{uptime.TotalDays:0.#} days" : $"{uptime.TotalHours:0.#} hours";

        // Fast Startup makes "Shut down" keep the kernel running, so an old uptime is common.
        return uptime.TotalDays >= 7
            ? new HealthItem(title, HealthLevel.Attention, detail,
                "Restart (not Shut down: Fast Startup keeps the session alive). It clears memory leaks and finishes pending updates.")
            : new HealthItem(title, HealthLevel.Good, detail);
    }

    private Hardware ReadHardware()
    {
        var result = _ctx.Runner.RunPowerShell("""
            $ram = @(Get-CimInstance Win32_PhysicalMemory | ForEach-Object { [ordered]@{ speed = $_.Speed; configured = $_.ConfiguredClockSpeed; gb = [math]::Round($_.Capacity / 1GB) } })
            $gpu = @(Get-CimInstance Win32_VideoController | ForEach-Object { [ordered]@{ name = $_.Name; date = $(if ($_.DriverDate) { $_.DriverDate.ToString('yyyy-MM-dd') } else { $null }) } })
            $disk = @(Get-PhysicalDisk | ForEach-Object { [ordered]@{ name = $_.FriendlyName; media = [string]$_.MediaType } })
            [ordered]@{ ram = $ram; gpu = $gpu; disk = $disk } | ConvertTo-Json -Depth 4 -Compress
            """);
        return result.Success ? Hardware.Parse(result.Output) : Hardware.Empty;
    }

    internal sealed record Ram(int Speed, int Configured, int Gb);
    internal sealed record Gpu(string Name, DateTime? Date);
    internal sealed record Disk(string Name, string Media);

    internal sealed class Hardware
    {
        public static readonly Hardware Empty = new();

        public List<Ram> Ram { get; } = new();
        public List<Gpu> Gpu { get; } = new();
        public List<Disk> Disks { get; } = new();

        public static Hardware Parse(string json)
        {
            var hardware = new Hardware();
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                foreach (var e in Items(root, "ram"))
                    hardware.Ram.Add(new Ram(Int(e, "speed"), Int(e, "configured"), Int(e, "gb")));

                foreach (var e in Items(root, "gpu"))
                {
                    DateTime? date = DateTime.TryParse(Text(e, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                    hardware.Gpu.Add(new Gpu(Text(e, "name") ?? "Graphics adapter", date));
                }

                foreach (var e in Items(root, "disk"))
                    hardware.Disks.Add(new Disk(Text(e, "name") ?? "Drive", Text(e, "media") is { Length: > 0 } m ? m : "unknown"));
            }
            catch (JsonException)
            {
                // Unreadable output means "unknown", which each check reports on its own.
            }
            return hardware;
        }

        // PowerShell turns a one-element array into a plain object in some versions; accept both.
        private static IEnumerable<JsonElement> Items(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value)) yield break;
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in value.EnumerateArray()) yield return e;
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                yield return value;
            }
        }

        private static int Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

        private static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}

public sealed class WindowsDisplayProbe : IDisplayProbe
{
    private const int CurrentSettings = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DevMode mode);

    public DisplayInfo? Primary()
    {
        var current = NewMode();
        if (!EnumDisplaySettingsW(null, CurrentSettings, ref current)) return null;

        var max = current.dmDisplayFrequency;
        for (var i = 0; ; i++)
        {
            var mode = NewMode();
            if (!EnumDisplaySettingsW(null, i, ref mode)) break;
            if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight && mode.dmBitsPerPel == current.dmBitsPerPel)
                max = Math.Max(max, mode.dmDisplayFrequency);
        }
        return new DisplayInfo(current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, max);
    }

    private static DevMode NewMode() => new() { dmSize = (short)Marshal.SizeOf<DevMode>(), dmDeviceName = "", dmFormName = "" };
}
