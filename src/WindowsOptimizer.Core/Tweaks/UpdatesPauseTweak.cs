namespace WindowsOptimizer.Core;

/// <summary>
/// Turns off automatic Windows Update by policy and sets the update services to manual start.
/// The service start modes are saved so Undo puts each one back exactly as it was.
/// </summary>
public sealed class UpdatesPauseTweak : RegistryTweak
{
    private const string AutoUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    private static readonly string[] Services = { "wuauserv", "UsoSvc", "BITS", "DoSvc" };

    public UpdatesPauseTweak(TweakContext ctx)
        : base(ctx, "updates-pause-auto", "Pause automatic updates",
            "Turns off automatic Windows Update downloads and installs and sets the update services to manual start. You can still update whenever you want.",
            TweakCategory.Updates, RiskLevel.High,
            Reg.LmDword(AutoUpdatePolicy, "NoAutoUpdate", 1),
            Reg.LmDword(AutoUpdatePolicy, "AUOptions", 2))
    {
    }

    protected override void OnBeforeApply(TweakSnapshot snapshot)
    {
        foreach (var service in Services)
            snapshot.Data["service:" + service] = ReadStartMode(service) ?? "";
    }

    protected override string? OnApplied(TweakSnapshot snapshot)
    {
        var failed = Services.Where(s => !Ctx.Runner.Run("sc.exe", $"config {s} start= demand").Success).ToList();
        return failed.Count == 0
            ? "Automatic updates paused. Restart the PC to finish."
            : "Updates paused, but these services could not be adjusted: " + string.Join(", ", failed);
    }

    protected override string? OnUndoing(TweakSnapshot snapshot)
    {
        foreach (var (key, mode) in snapshot.Data.Where(e => e.Key.StartsWith("service:", StringComparison.Ordinal)))
        {
            if (mode.Length > 0) Ctx.Runner.Run("sc.exe", $"config {key["service:".Length..]} start= {mode}");
        }
        return "Automatic updates restored. Restart the PC to finish.";
    }

    /// <summary>The start type of each update service as the Service Control Manager reports it.</summary>
    public override string? Fingerprint() =>
        string.Join("; ", Services.Select(s => $"{s}={ServiceStartType(s)}"));

    private string ServiceStartType(string service)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            Ctx.Runner.Run("sc.exe", $"qc {service}").Output, @"START_TYPE\s*:\s*(.+)");
        return match.Success ? match.Groups[1].Value.Trim() : "unknown";
    }

    /// <summary>Reads the start mode in the vocabulary of <c>sc config</c>.</summary>
    private string? ReadStartMode(string service)
    {
        var key = $@"SYSTEM\CurrentControlSet\Services\{service}";
        var start = Ctx.Registry.GetValue(RegHive.LocalMachine, key, "Start");
        if (start is not { Kind: RegKind.DWord }) return null;

        var delayed = Ctx.Registry.GetValue(RegHive.LocalMachine, key, "DelayedAutostart") is { Kind: RegKind.DWord, Data: 1 };
        return (int)start.Value.Data switch
        {
            2 => delayed ? "delayed-auto" : "auto",
            3 => "demand",
            4 => "disabled",
            _ => null,
        };
    }
}
