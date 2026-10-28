using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>
/// What Windows has installed as optional capabilities and optional features. Asking is slow (a PowerShell call
/// that can take seconds), so each answer is read once and kept until a tweak changes something.
/// </summary>
internal sealed class ComponentCache
{
    private static readonly ConditionalWeakTable<ICommandRunner, ComponentCache> Caches = new();

    private readonly ICommandRunner _runner;
    private readonly object _lock = new();
    private Dictionary<string, string>? _capabilities;
    private Dictionary<string, string>? _features;

    private ComponentCache(ICommandRunner runner) => _runner = runner;

    public static ComponentCache For(ICommandRunner runner) => Caches.GetValue(runner, r => new ComponentCache(r));

    public IReadOnlyDictionary<string, string> Capabilities()
    {
        lock (_lock)
        {
            return _capabilities ??= Read("Get-WindowsCapability -Online | ForEach-Object { $_.Name + '|' + $_.State }");
        }
    }

    public IReadOnlyDictionary<string, string> Features()
    {
        lock (_lock)
        {
            return _features ??= Read("Get-WindowsOptionalFeature -Online | ForEach-Object { $_.FeatureName + '|' + $_.State }");
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _capabilities = null;
            _features = null;
        }
    }

    private Dictionary<string, string> Read(string script)
    {
        var result = _runner.RunPowerShell(script, timeoutMs: 600_000);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!result.Success) return map;

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = line.LastIndexOf('|');
            if (bar > 0) map[line[..bar]] = line[(bar + 1)..];
        }
        return map;
    }
}

public static class WindowsComponents
{
    /// <summary>Forget what was read about installed components, so the next status check asks Windows again.</summary>
    public static void Invalidate(TweakContext ctx) => ComponentCache.For(ctx.Runner).Invalidate();

    /// <summary>Capability and feature names go into a PowerShell script, so they must be plain.</summary>
    internal static bool IsPlainName(string name) => Regex.IsMatch(name, @"^[A-Za-z0-9._~-]+$");
}

/// <summary>
/// Removes optional Windows capabilities (handwriting, speech, Quick Assist...). It remembers which ones it removed,
/// so Undo adds back exactly those (Windows may need an internet connection for that).
/// </summary>
public sealed class CapabilityTweak : TweakBase
{
    private readonly string[] _prefixes;

    /// <param name="prefixes">Capability names, or the start of a name (for example "Language.Handwriting").</param>
    public CapabilityTweak(
        TweakContext ctx, string id, string name, string description, RiskLevel risk, params string[] prefixes)
        : base(ctx, id, name, description, TweakCategory.System, risk)
    {
        _prefixes = prefixes;
        NeedsAdmin = true;
        SlowOperation = true;
        UnavailableReason = "This version of Windows doesn't have this component.";
    }

    private List<KeyValuePair<string, string>> Matches() =>
        ComponentCache.For(Ctx.Runner).Capabilities()
            .Where(c => _prefixes.Any(p => c.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    private List<string> Installed() =>
        Matches().Where(m => m.Value.Equals("Installed", StringComparison.OrdinalIgnoreCase)).Select(m => m.Key).ToList();

    /// <summary>Applied means "removed". Unknown means this Windows doesn't offer the component at all.</summary>
    public override TweakStatus GetStatus()
    {
        if (Matches().Count == 0) return TweakStatus.Unknown;
        return Installed().Count == 0 ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public override string? Fingerprint()
    {
        WindowsComponents.Invalidate(Ctx);
        return string.Join(",", Installed());
    }

    public override IReadOnlyList<PreviewLine> Preview()
    {
        var installed = Installed();
        return new[]
        {
            new PreviewLine("Installed components", installed.Count == 0 ? "none" : $"{installed.Count} installed", "removed",
                string.Join(", ", installed)),
        };
    }

    public override TweakResult Apply()
    {
        var names = Installed();
        if (names.Count == 0) return TweakResult.Ok("Nothing to remove: it was not installed.");
        if (names.Any(n => !WindowsComponents.IsPlainName(n))) return TweakResult.Fail("A component has an unexpected name; nothing was changed.");

        var snapshot = Ctx.State.Load(Id) ?? new TweakSnapshot { TweakId = Id };
        snapshot.Data["names"] = string.Join(";", names.Union(snapshot.Data.TryGetValue("names", out var old) ? old.Split(';', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>()));
        Ctx.State.Save(snapshot);

        var list = string.Join(",", names.Select(n => $"'{n}'"));
        var result = Ctx.Runner.RunPowerShell($"foreach ($n in @({list})) {{ Remove-WindowsCapability -Online -Name $n | Out-Null }}", timeoutMs: 1_800_000);
        WindowsComponents.Invalidate(Ctx);

        return result.Success
            ? TweakResult.Ok($"Removed {names.Count} component(s).")
            : TweakResult.Fail(result.Text);
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null || !snapshot.Data.TryGetValue("names", out var joined))
            return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        var names = joined.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (names.Any(n => !WindowsComponents.IsPlainName(n))) return TweakResult.Fail("The backup has an unexpected name; nothing was changed.");

        var list = string.Join(",", names.Select(n => $"'{n}'"));
        var result = Ctx.Runner.RunPowerShell($"foreach ($n in @({list})) {{ Add-WindowsCapability -Online -Name $n | Out-Null }}", timeoutMs: 1_800_000);
        WindowsComponents.Invalidate(Ctx);
        if (!result.Success) return TweakResult.Fail(result.Text);

        Ctx.State.Delete(Id);
        return TweakResult.Ok($"Installed {names.Length} component(s) again.");
    }
}

/// <summary>Turns an optional Windows feature off (Recall, the Remote Desktop client) or on (.NET Framework 3.5).</summary>
public sealed class OptionalFeatureTweak : TweakBase
{
    private readonly string _feature;
    private readonly bool _enable;

    /// <param name="enable">True when applying the tweak installs the feature; false when it removes it.</param>
    public OptionalFeatureTweak(
        TweakContext ctx, string id, string name, string description, RiskLevel risk, string feature, bool enable, string unavailableReason,
        TweakCategory category = TweakCategory.System)
        : base(ctx, id, name, description, category, risk)
    {
        _feature = feature;
        _enable = enable;
        NeedsAdmin = true;
        SlowOperation = true;
        NeedsRestart = true;
        UnavailableReason = unavailableReason;
    }

    private string? State() =>
        ComponentCache.For(Ctx.Runner).Features().TryGetValue(_feature, out var state) ? state : null;

    public override TweakStatus GetStatus()
    {
        if (State() is not { } state) return TweakStatus.Unknown;
        var on = state.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
        return on == _enable ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public override string? Fingerprint()
    {
        WindowsComponents.Invalidate(Ctx);
        return State();
    }

    public override IReadOnlyList<PreviewLine> Preview() =>
        new[] { new PreviewLine($"Windows feature {_feature}", State() ?? "not present", _enable ? "Enabled" : "Disabled") };

    public override TweakResult Apply()
    {
        if (!WindowsComponents.IsPlainName(_feature)) return TweakResult.Fail("Unexpected feature name.");

        // The marker is what makes Undo available: the feature was changed by this app.
        Ctx.State.Save(new TweakSnapshot { TweakId = Id });

        var script = _enable
            ? $"Enable-WindowsOptionalFeature -Online -FeatureName {_feature} -All -NoRestart | Out-Null"
            : $"Disable-WindowsOptionalFeature -Online -FeatureName {_feature} -NoRestart | Out-Null";
        var result = Ctx.Runner.RunPowerShell(script, timeoutMs: 1_800_000);
        WindowsComponents.Invalidate(Ctx);

        if (!result.Success)
        {
            Ctx.State.Delete(Id);
            return TweakResult.Fail(result.Text);
        }
        return TweakResult.Ok("Done. Restart the PC to finish.");
    }

    public override TweakResult Undo()
    {
        if (Ctx.State.Load(Id) is null) return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        var script = _enable
            ? $"Disable-WindowsOptionalFeature -Online -FeatureName {_feature} -NoRestart | Out-Null"
            : $"Enable-WindowsOptionalFeature -Online -FeatureName {_feature} -All -NoRestart | Out-Null";
        var result = Ctx.Runner.RunPowerShell(script, timeoutMs: 1_800_000);
        WindowsComponents.Invalidate(Ctx);
        if (!result.Success) return TweakResult.Fail(result.Text);

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Restored. Restart the PC to finish.");
    }
}
