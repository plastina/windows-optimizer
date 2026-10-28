using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>
/// Sets one power plan setting (a sub-group and a setting from <c>powercfg</c>) on the active plan.
/// The previous values of each plan it touched are saved, so Undo puts back exactly what the plan had.
/// </summary>
public sealed class PowerSettingTweak : TweakBase
{
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
    private static readonly Regex HexValue = new("0x[0-9a-fA-F]{8}");

    private readonly string _subGroup;
    private readonly string _setting;
    private readonly uint _ac;
    private readonly uint? _dc;
    private readonly string _label;

    /// <param name="subGroup">Sub-group alias or GUID, as <c>powercfg</c> accepts it.</param>
    /// <param name="setting">Setting alias or GUID.</param>
    /// <param name="ac">Value to set while plugged in.</param>
    /// <param name="dc">Value to set on battery, or null to leave the battery value alone.</param>
    /// <param name="label">What the setting is called in the "what changes" table.</param>
    public PowerSettingTweak(
        TweakContext ctx, string id, string name, string description, RiskLevel risk,
        string subGroup, string setting, uint ac, uint? dc, string label)
        : base(ctx, id, name, description, TweakCategory.Power, risk)
    {
        NeedsAdmin = true;
        _subGroup = subGroup;
        _setting = setting;
        _ac = ac;
        _dc = dc;
        _label = label;
    }

    public override TweakStatus GetStatus()
    {
        var scheme = ActiveScheme();
        var values = scheme is null ? null : Read(scheme);
        if (values is null) return TweakStatus.Unknown;
        return values.Value.Ac == _ac && (_dc is null || values.Value.Dc == _dc) ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    /// <summary>The active plan and the setting's current values, straight from powercfg.</summary>
    public override string? Fingerprint()
    {
        var scheme = ActiveScheme();
        if (scheme is null) return null;
        return scheme + (Read(scheme) is { } v ? $" ac={v.Ac} dc={v.Dc}" : " (unreadable)");
    }

    public override IReadOnlyList<PreviewLine> Preview()
    {
        var scheme = ActiveScheme();
        var values = scheme is null ? null : Read(scheme);
        var current = values is { } v ? $"AC {v.Ac}, battery {v.Dc}" : "—";
        var target = _dc is null ? $"AC {_ac}" : $"AC {_ac}, battery {_dc}";
        return new[] { new PreviewLine(_label, current, target) };
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
            if (before is null) return TweakResult.Fail($"Could not read the power setting \"{_label}\".");

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

        Run($"/setacvalueindex {scheme} {_subGroup} {_setting} {_ac}");
        if (_dc is { } dc) Run($"/setdcvalueindex {scheme} {_subGroup} {_setting} {dc}");
        Run($"/setactive {scheme}");

        return errors.Count == 0
            ? TweakResult.Ok($"{_label} set.")
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
            Run($"/setacvalueindex {scheme} {_subGroup} {_setting} {snapshot.Data[key]}");
            Run($"/setdcvalueindex {scheme} {_subGroup} {_setting} {snapshot.Data["dc:" + scheme]}");
            if (string.Equals(scheme, active, StringComparison.OrdinalIgnoreCase)) Run($"/setactive {scheme}");
        }

        if (errors.Count > 0) return TweakResult.Fail(string.Join(Environment.NewLine, errors));

        Ctx.State.Delete(Id);
        return TweakResult.Ok($"{_label} restored.");
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
        var result = Ctx.Runner.Run("powercfg", $"/query {scheme} {_subGroup} {_setting}");
        if (!result.Success) return null;

        // The last two hex numbers are always the AC and DC indexes, whatever the Windows language.
        var hex = HexValue.Matches(result.Output);
        if (hex.Count < 2) return null;
        return (ParseHex(hex[^2].Value), ParseHex(hex[^1].Value));
    }

    private static uint ParseHex(string value) =>
        uint.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
