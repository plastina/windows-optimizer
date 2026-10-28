using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>Sets one top-level value in an app's JSON settings file (for example Discord's settings.json).</summary>
public sealed class JsonSettingTweak : TweakBase
{
    private readonly string _file;
    private readonly string _key;
    private readonly bool _value;

    public JsonSettingTweak(
        TweakContext ctx, string id, string name, string description, string file, string key, bool value, string notInstalledReason)
        : base(ctx, id, name, description, TweakCategory.Startup, RiskLevel.Low)
    {
        _file = file;
        _key = key;
        _value = value;
        UnavailableReason = notInstalledReason;
    }

    public override bool IsAvailable() => File.Exists(_file);

    private JsonObject? Read()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(_file)) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public override TweakStatus GetStatus()
    {
        if (Read() is not { } root) return TweakStatus.Unknown;
        return root[_key] is JsonValue v && v.TryGetValue<bool>(out var now) && now == _value ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public override IReadOnlyList<PreviewLine> Preview() =>
        new[] { new PreviewLine(_key, Read()?[_key]?.ToJsonString() ?? "(not set)", _value ? "true" : "false", _file) };

    public override TweakResult Apply()
    {
        if (Read() is not { } root) return TweakResult.Fail("Could not read the settings file.");

        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null)
        {
            snapshot = new TweakSnapshot { TweakId = Id };
            snapshot.Data["previous"] = root[_key]?.ToJsonString() ?? "\0absent";
            Ctx.State.Save(snapshot);
        }

        root[_key] = _value;
        return Write(root) ?? TweakResult.Ok("Done. Close and reopen the app for it to take effect.");
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null || !snapshot.Data.TryGetValue("previous", out var previous))
            return TweakResult.Fail("No backup for this tweak; the app did not apply it.");
        if (Read() is not { } root) return TweakResult.Fail("Could not read the settings file.");

        if (previous == "\0absent") root.Remove(_key);
        else root[_key] = JsonNode.Parse(previous);

        if (Write(root) is { } failure) return failure;
        Ctx.State.Delete(Id);
        return TweakResult.Ok("Restored.");
    }

    private TweakResult? Write(JsonObject root)
    {
        try
        {
            File.WriteAllText(_file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TweakResult.Fail($"Could not write the settings file: {ex.Message}");
        }
    }
}

/// <summary>
/// Deletes registry keys, after exporting each one with <c>reg export</c>. Undo imports the exported files back,
/// so the keys return with every value and type they had.
/// </summary>
public sealed class RegistryKeyDeleteTweak : TweakBase
{
    private readonly RegHive _hive;
    private readonly string[] _keys;

    public RegistryKeyDeleteTweak(
        TweakContext ctx, string id, string name, string description, TweakCategory category, RiskLevel risk, RegHive hive, params string[] subKeys)
        : base(ctx, id, name, description, category, risk)
    {
        _hive = hive;
        _keys = subKeys;
        NeedsAdmin = hive != RegHive.CurrentUser;
    }

    private string Prefix => _hive == RegHive.CurrentUser ? "HKCU" : "HKLM";

    /// <summary>Applied means the keys are gone.</summary>
    public override TweakStatus GetStatus()
    {
        var present = _keys.Count(k => Ctx.Registry.KeyExists(_hive, k));
        return present == 0 ? TweakStatus.Applied : present == _keys.Length ? TweakStatus.NotApplied : TweakStatus.Partial;
    }

    public override IReadOnlyList<PreviewLine> Preview() =>
        _keys.Select(k => new PreviewLine("Registry key", Ctx.Registry.KeyExists(_hive, k) ? "present" : "absent", "deleted", $@"{Prefix}\{k}")).ToList();

    public override TweakResult Apply()
    {
        var snapshot = Ctx.State.Load(Id) ?? new TweakSnapshot { TweakId = Id };
        var folder = Path.Combine(Ctx.DataRoot, "state");
        Directory.CreateDirectory(folder);

        var errors = new List<string>();
        for (var i = 0; i < _keys.Length; i++)
        {
            if (!Ctx.Registry.KeyExists(_hive, _keys[i])) continue;

            // Export first: the backup must exist before anything is deleted.
            var file = Path.Combine(folder, $"{Id}-{i}.reg");
            var export = Ctx.Runner.Run("reg.exe", $"export \"{Prefix}\\{_keys[i]}\" \"{file}\" /y");
            if (!export.Success)
            {
                errors.Add($"{_keys[i]}: could not back it up ({export.Text})");
                continue;
            }

            snapshot.Data["file:" + i] = file;
            Ctx.State.Save(snapshot);

            try
            {
                Ctx.Registry.DeleteKeyTree(_hive, _keys[i]);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                errors.Add($"{_keys[i]}: {ex.Message}");
            }
        }

        if (errors.Count > 0) return TweakResult.Fail(string.Join("; ", errors));
        if (snapshot.Data.Count == 0) return TweakResult.Ok("The keys were already absent.");
        return TweakResult.Ok("Done.");
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null) return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        foreach (var (key, file) in snapshot.Data.Where(d => d.Key.StartsWith("file:", StringComparison.Ordinal)).ToList())
        {
            var import = Ctx.Runner.Run("reg.exe", $"import \"{file}\"");
            if (!import.Success) return TweakResult.Fail($"Could not restore from {file}: {import.Text}");
            File.Delete(file);
        }

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Restored.");
    }
}

/// <summary>Renames the current entry of the boot menu (shown when Windows starts); Undo puts the old name back.</summary>
public sealed class BootMenuNameTweak : TweakBase
{
    private static readonly Regex DescriptionLine = new(@"^description\s+(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private const string Suffix = " (Windows Optimizer)";

    public BootMenuNameTweak(TweakContext ctx)
        : base(ctx, "boot-menu-name", "Name the boot menu entry",
            "Adds \"(Windows Optimizer)\" to the name of the current Windows entry in the boot menu. Useful to tell installs apart on a PC with more than one Windows.",
            TweakCategory.System, RiskLevel.Low)
    {
        NeedsAdmin = true;
    }

    private string? Current() =>
        DescriptionLine.Match(Ctx.Runner.Run("bcdedit.exe", "/enum {current}").Output) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    public override TweakStatus GetStatus() =>
        Current() is { } now ? (now.EndsWith(Suffix, StringComparison.Ordinal) ? TweakStatus.Applied : TweakStatus.NotApplied) : TweakStatus.Unknown;

    public override string? Fingerprint() => Current();

    public override IReadOnlyList<PreviewLine> Preview() =>
        new[] { new PreviewLine("Boot menu entry", Current() ?? "—", (Current() ?? "Windows") + Suffix, "Boot configuration (bcdedit)") };

    public override TweakResult Apply()
    {
        var now = Current();
        if (now is null) return TweakResult.Fail("Could not read the boot menu entry.");
        if (now.EndsWith(Suffix, StringComparison.Ordinal)) return TweakResult.Ok("The entry already has that name.");
        if (now.Contains('"')) return TweakResult.Fail("The current name has quotes in it; the app won't change it.");

        var snapshot = Ctx.State.Load(Id) ?? new TweakSnapshot { TweakId = Id };
        snapshot.Data.TryAdd("description", now);
        Ctx.State.Save(snapshot);

        var result = Ctx.Runner.Run("bcdedit.exe", $"/set {{current}} description \"{now}{Suffix}\"");
        if (result.Success) return TweakResult.Ok("Done.");

        Ctx.State.Delete(Id);
        return TweakResult.Fail(result.Text);
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null || !snapshot.Data.TryGetValue("description", out var previous))
            return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        var result = Ctx.Runner.Run("bcdedit.exe", $"/set {{current}} description \"{previous}\"");
        if (!result.Success) return TweakResult.Fail(result.Text);

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Restored.");
    }
}
