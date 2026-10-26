namespace WindowsOptimizer.Core;

public sealed record RegistryEdit(
    RegHive Hive, string SubKey, string Name, RegValue Value, bool OnlyIfKeyExists = false)
{
    public string HivePrefix => Hive == RegHive.CurrentUser ? "HKCU" : "HKLM";
    public string FullKey => $@"{HivePrefix}\{SubKey}";
    public string Display => $@"{FullKey}\{Name}";
}

/// <summary>Short constructors so the catalog reads like a list of settings.</summary>
public static class Reg
{
    public static RegistryEdit CuDword(string subKey, string name, int value, bool onlyIfKeyExists = false) =>
        new(RegHive.CurrentUser, subKey, name, RegValue.DWord(value), onlyIfKeyExists);

    public static RegistryEdit LmDword(string subKey, string name, int value, bool onlyIfKeyExists = false) =>
        new(RegHive.LocalMachine, subKey, name, RegValue.DWord(value), onlyIfKeyExists);

    public static RegistryEdit CuString(string subKey, string name, string value) =>
        new(RegHive.CurrentUser, subKey, name, RegValue.Str(value));

    public static RegistryEdit LmString(string subKey, string name, string value) =>
        new(RegHive.LocalMachine, subKey, name, RegValue.Str(value));
}

/// <summary>
/// A tweak made of registry values. Apply remembers what was there before; Undo puts it back
/// (including deleting values and keys that did not exist).
/// </summary>
public class RegistryTweak : TweakBase
{
    public RegistryTweak(
        TweakContext ctx, string id, string name, string description,
        TweakCategory category, RiskLevel risk, params RegistryEdit[] edits)
        : base(ctx, id, name, description, category, risk)
    {
        Edits = edits;
    }

    protected IReadOnlyList<RegistryEdit> Edits { get; }

    private List<RegistryEdit> ActiveEdits() =>
        Edits.Where(e => !e.OnlyIfKeyExists || Ctx.Registry.KeyExists(e.Hive, e.SubKey)).ToList();

    public override TweakStatus GetStatus()
    {
        var edits = ActiveEdits();
        if (edits.Count == 0) return TweakStatus.Unknown;

        var matching = edits.Count(e => Ctx.Registry.GetValue(e.Hive, e.SubKey, e.Name) == e.Value);
        return matching == edits.Count ? TweakStatus.Applied
            : matching == 0 ? TweakStatus.NotApplied
            : TweakStatus.Partial;
    }

    public override IReadOnlyList<PreviewLine> Preview() =>
        ActiveEdits()
            .Select(e => new PreviewLine(
                e.Name,
                Ctx.Registry.GetValue(e.Hive, e.SubKey, e.Name)?.ToString() ?? "(ausente)",
                e.Value.ToString(),
                e.FullKey))
            .ToList();

    public override TweakResult Apply()
    {
        var edits = ActiveEdits();
        var existing = Ctx.State.Load(Id);
        var snapshot = existing ?? new TweakSnapshot { TweakId = Id };

        // Keep the first snapshot: applying twice must never overwrite the user's original values.
        if (existing is null && edits.Count > 0)
        {
            foreach (var edit in edits) snapshot.Registry.Add(Capture(edit));
            OnBeforeApply(snapshot);
            Ctx.State.Save(snapshot);
        }

        var errors = new List<string>();
        foreach (var edit in edits)
        {
            try
            {
                Ctx.Registry.SetValue(edit.Hive, edit.SubKey, edit.Name, edit.Value);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                errors.Add($"{edit.Display}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            if (existing is null && errors.Count == edits.Count) Ctx.State.Delete(Id);
            return TweakResult.Fail("Não foi possível gravar: " + string.Join("; ", errors));
        }

        return TweakResult.Ok(OnApplied(snapshot) ?? "Aplicado.");
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null) return TweakResult.Fail("Não há backup deste ajuste; o app não o aplicou.");

        var errors = new List<string>();
        foreach (var entry in snapshot.Registry)
        {
            try
            {
                if (entry.ToValue() is { } previous)
                    Ctx.Registry.SetValue(entry.Hive, entry.SubKey, entry.Name, previous);
                else
                    Ctx.Registry.DeleteValue(entry.Hive, entry.SubKey, entry.Name);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                errors.Add($"{entry.Name}: {ex.Message}");
            }
        }

        // Remove keys this tweak created, deepest first, and only if nothing else lives in them.
        foreach (var entry in snapshot.Registry
                     .Where(e => !e.KeyExisted)
                     .DistinctBy(e => (e.Hive, e.SubKey))
                     .OrderByDescending(e => e.SubKey.Length))
        {
            try { Ctx.Registry.DeleteKeyIfEmpty(entry.Hive, entry.SubKey); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }

        var extra = OnUndoing(snapshot);

        if (errors.Count > 0) return TweakResult.Fail("Não foi possível restaurar: " + string.Join("; ", errors));

        Ctx.State.Delete(Id);
        return TweakResult.Ok(extra ?? "Restaurado ao valor anterior.");
    }

    /// <summary>Lets subclasses record extra state (services, files...) before anything changes.</summary>
    protected virtual void OnBeforeApply(TweakSnapshot snapshot) { }

    /// <summary>Runs after the registry values are written. Return text to show the user.</summary>
    protected virtual string? OnApplied(TweakSnapshot snapshot) => null;

    /// <summary>Runs during Undo, after registry values are restored. Return text to show the user.</summary>
    protected virtual string? OnUndoing(TweakSnapshot snapshot) => null;

    private RegistrySnapshotEntry Capture(RegistryEdit edit)
    {
        var keyExisted = Ctx.Registry.KeyExists(edit.Hive, edit.SubKey);
        var value = keyExisted ? Ctx.Registry.GetValue(edit.Hive, edit.SubKey, edit.Name) : null;
        return new RegistrySnapshotEntry
        {
            Hive = edit.Hive,
            SubKey = edit.SubKey,
            Name = edit.Name,
            KeyExisted = keyExisted,
            ValueExisted = value is not null,
            Kind = value?.Kind ?? edit.Value.Kind,
            Text = value is { Kind: RegKind.String } s ? (string)s.Data : null,
            Number = value is { Kind: RegKind.DWord } d ? (int)d.Data : 0,
        };
    }
}
