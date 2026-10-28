using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>
/// Sets PCI Express link power saving (ASPM) to Off on the active power plan, on AC and on battery.
/// Each plan's previous values are saved, so Undo puts back what the plan had.
/// </summary>
public sealed class PcieAspmTweak : TweakBase
{
    private const string SubGroup = "SUB_PCIEXPRESS";
    private const string Setting = "ASPM";

    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
    private static readonly Regex HexValue = new("0x[0-9a-fA-F]{8}");

    public PcieAspmTweak(TweakContext ctx)
        : base(ctx, "pcie-aspm-off", "Turn off PCIe power saving",
            "Stops the active power plan from putting PCIe devices and slots into a low-power state. It removes a small wake-up delay for graphics cards and other devices, at the cost of a little more idle power.",
            TweakCategory.Power, RiskLevel.Low)
    {
        NeedsAdmin = true;
    }

    public override TweakStatus GetStatus()
    {
        var scheme = ActiveScheme();
        if (scheme is null) return TweakStatus.Unknown;

        var values = Read(scheme);
        if (values is null) return TweakStatus.Unknown;
        return values.Value is (0, 0) ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    /// <summary>The active plan and its current PCIe power setting, straight from powercfg.</summary>
    public override string? Fingerprint()
    {
        var scheme = ActiveScheme();
        if (scheme is null) return null;

        var values = Read(scheme);
        return scheme + (values is { } v ? $" ac={v.Ac} dc={v.Dc}" : " (unreadable)");
    }

    public override IReadOnlyList<PreviewLine> Preview()
    {
        var scheme = ActiveScheme();
        var values = scheme is null ? null : Read(scheme);
        var current = values is { } v ? $"AC {v.Ac}, battery {v.Dc}" : "—";
        return new[] { new PreviewLine("PCIe power saving (ASPM)", current, "AC 0, battery 0") };
    }

    public override TweakResult Apply()
    {
        var scheme = ActiveScheme();
        if (scheme is null) return TweakResult.Fail("Could not read the active power plan.");

        var snapshot = Ctx.State.Load(Id) ?? new TweakSnapshot { TweakId = Id };

        // Keep the first backup of each plan: applying twice must never overwrite the original values.
        if (!snapshot.Data.ContainsKey("ac:" + scheme))
        {
            var before = Read(scheme);
            if (before is null) return TweakResult.Fail("Could not read the PCIe power setting.");

            snapshot.Data["ac:" + scheme] = before.Value.Ac.ToString(CultureInfo.InvariantCulture);
            snapshot.Data["dc:" + scheme] = before.Value.Dc.ToString(CultureInfo.InvariantCulture);
            Ctx.State.Save(snapshot);
        }

        var errors = new List<string>();
        void Run(string args)
        {
            var result = Ctx.Runner.Run("powercfg", args);
            if (!result.Success) errors.Add($"powercfg {args}: {result.Text}");
        }

        Run($"/setacvalueindex {scheme} {SubGroup} {Setting} 0");
        Run($"/setdcvalueindex {scheme} {SubGroup} {Setting} 0");
        Run($"/setactive {scheme}");

        return errors.Count == 0
            ? TweakResult.Ok("PCIe power saving turned off.")
            : TweakResult.Fail(string.Join(Environment.NewLine, errors));
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null) return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        var active = ActiveScheme();
        var errors = new List<string>();
        void Run(string args)
        {
            var result = Ctx.Runner.Run("powercfg", args);
            if (!result.Success) errors.Add($"powercfg {args}: {result.Text}");
        }

        foreach (var key in snapshot.Data.Keys.Where(k => k.StartsWith("ac:", StringComparison.Ordinal)).ToList())
        {
            var scheme = key[3..];
            Run($"/setacvalueindex {scheme} {SubGroup} {Setting} {snapshot.Data[key]}");
            Run($"/setdcvalueindex {scheme} {SubGroup} {Setting} {snapshot.Data["dc:" + scheme]}");
            if (string.Equals(scheme, active, StringComparison.OrdinalIgnoreCase)) Run($"/setactive {scheme}");
        }

        if (errors.Count > 0) return TweakResult.Fail(string.Join(Environment.NewLine, errors));

        Ctx.State.Delete(Id);
        return TweakResult.Ok("PCIe power saving restored.");
    }

    private string? ActiveScheme()
    {
        var result = Ctx.Runner.Run("powercfg", "/getactivescheme");
        if (!result.Success) return null;

        var match = GuidPattern.Match(result.Output);
        return match.Success ? match.Value : null;
    }

    private (uint Ac, uint Dc)? Read(string scheme)
    {
        var result = Ctx.Runner.Run("powercfg", $"/query {scheme} {SubGroup} {Setting}");
        if (!result.Success) return null;

        // The last two hex numbers are always the AC and DC indexes, whatever the Windows language.
        var hex = HexValue.Matches(result.Output);
        if (hex.Count < 2) return null;

        return (ParseHex(hex[^2].Value), ParseHex(hex[^1].Value));
    }

    private static uint ParseHex(string value) =>
        uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
