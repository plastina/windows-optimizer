using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>
/// Switches to the Ultimate Performance plan (falling back to High performance) and stops the screen,
/// sleep and hibernate timers on AC power. Undo restores the previous plan and the timers.
/// </summary>
public sealed class PowerPlanTweak : TweakBase
{
    private const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
    // Matches the English and Brazilian Portuguese names of the plan ("Ultimate Performance", "Desempenho Máximo").
    private static readonly Regex MaxPlanName = new("Ultimate|Máxim|Maxim", RegexOptions.IgnoreCase);
    private static readonly Regex HexValue = new("0x[0-9a-fA-F]{8}");

    // Aliases work on every Windows language; the Key is how the value is stored in the snapshot.
    private static readonly (string Key, string SubGroup, string Setting)[] Timers =
    {
        ("monitor", "SUB_VIDEO", "VIDEOIDLE"),
        ("standby", "SUB_SLEEP", "STANDBYIDLE"),
        ("hibernate", "SUB_SLEEP", "HIBERNATEIDLE"),
        ("consolelock", "SUB_NONE", "CONSOLELOCK"),
    };

    public PowerPlanTweak(TweakContext ctx)
        : base(ctx, "power-plan-max", "Ultimate Performance power plan",
            "Turns on the highest-performance plan and keeps the screen, sleep and hibernation from turning the PC off while it is plugged in.",
            TweakCategory.Power, RiskLevel.Low)
    {
    }

    public override TweakStatus GetStatus()
    {
        var active = GetActive();
        if (active is null) return TweakStatus.Unknown;
        return IsMaxPlan(active.Value) ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public override IReadOnlyList<PreviewLine> Preview() => new[]
    {
        new PreviewLine("Active power plan", GetActive()?.Name ?? "—", "Ultimate Performance"),
        new PreviewLine("Turn off screen, sleep and hibernate (plugged in)", "—", "Never"),
        new PreviewLine("Automatic console lock", "—", "Off"),
    };

    public override TweakResult Apply()
    {
        var active = GetActive();
        if (active is null) return TweakResult.Fail("Could not read the active power plan.");

        var snapshot = Ctx.State.Load(Id) ?? new TweakSnapshot { TweakId = Id };
        snapshot.Data.TryAdd("previousScheme", active.Value.Guid);

        if (!snapshot.Data.TryGetValue("targetScheme", out var target) || !SchemeExists(target))
        {
            snapshot.Data.Remove("createdScheme");
            if (IsMaxPlan(active.Value))
            {
                target = active.Value.Guid;
            }
            else
            {
                target = Duplicate();
                if (target is null) target = HighPerformance;
                else snapshot.Data["createdScheme"] = "1";
            }

            snapshot.Data["targetScheme"] = target;
            CaptureTimers(snapshot, target);
        }

        Ctx.State.Save(snapshot);

        var errors = new List<string>();
        void Run(string args)
        {
            var result = Ctx.Runner.Run("powercfg", args);
            if (!result.Success) errors.Add($"powercfg {args}: {result.Text}");
        }

        Run($"/setactive {target}");
        foreach (var (_, subGroup, setting) in Timers)
            Run($"/setacvalueindex {target} {subGroup} {setting} 0");
        Run($"/setdcvalueindex {target} SUB_NONE CONSOLELOCK 0");
        Run($"/setactive {target}");

        return errors.Count == 0
            ? TweakResult.Ok("Maximum performance plan turned on.")
            : TweakResult.Fail(string.Join(Environment.NewLine, errors));
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null) return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        snapshot.Data.TryGetValue("targetScheme", out var target);
        snapshot.Data.TryGetValue("previousScheme", out var previous);
        var created = snapshot.Data.ContainsKey("createdScheme");

        // A scheme we duplicated is deleted below, so only restore timers on a scheme that was already there.
        if (target is not null && !created && SchemeExists(target))
        {
            foreach (var (key, subGroup, setting) in Timers)
            {
                if (snapshot.Data.TryGetValue("ac:" + key, out var ac))
                    Ctx.Runner.Run("powercfg", $"/setacvalueindex {target} {subGroup} {setting} {ac}");
                if (snapshot.Data.TryGetValue("dc:" + key, out var dc))
                    Ctx.Runner.Run("powercfg", $"/setdcvalueindex {target} {subGroup} {setting} {dc}");
            }
        }

        if (previous is not null && SchemeExists(previous))
            Ctx.Runner.Run("powercfg", $"/setactive {previous}");

        if (created && target is not null && !string.Equals(target, previous, StringComparison.OrdinalIgnoreCase))
            Ctx.Runner.Run("powercfg", $"/delete {target}");

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Previous power plan restored.");
    }

    private (string Guid, string Name)? GetActive()
    {
        var result = Ctx.Runner.Run("powercfg", "/getactivescheme");
        if (!result.Success) return null;

        var match = GuidPattern.Match(result.Output);
        if (!match.Success) return null;

        var open = result.Output.LastIndexOf('(');
        var close = result.Output.LastIndexOf(')');
        var name = open >= 0 && close > open ? result.Output[(open + 1)..close] : "";
        return (match.Value, name);
    }

    private static bool IsMaxPlan((string Guid, string Name) plan) =>
        MaxPlanName.IsMatch(plan.Name)
        || string.Equals(plan.Guid, HighPerformance, StringComparison.OrdinalIgnoreCase);

    private bool SchemeExists(string guid) =>
        Ctx.Runner.Run("powercfg", "/list").Output.Contains(guid, StringComparison.OrdinalIgnoreCase);

    private string? Duplicate()
    {
        var result = Ctx.Runner.Run("powercfg", $"/duplicatescheme {UltimateTemplate}");
        if (!result.Success) return null;
        var match = GuidPattern.Match(result.Output);
        return match.Success ? match.Value : null;
    }

    private void CaptureTimers(TweakSnapshot snapshot, string scheme)
    {
        foreach (var (key, subGroup, setting) in Timers)
        {
            var result = Ctx.Runner.Run("powercfg", $"/query {scheme} {subGroup} {setting}");
            if (!result.Success) continue;

            // The last two hex numbers are always the AC and DC indexes, whatever the Windows language.
            var hex = HexValue.Matches(result.Output);
            if (hex.Count < 2) continue;

            snapshot.Data["ac:" + key] = ParseHex(hex[^2].Value);
            snapshot.Data["dc:" + key] = ParseHex(hex[^1].Value);
        }
    }

    private static string ParseHex(string value) =>
        uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
}
