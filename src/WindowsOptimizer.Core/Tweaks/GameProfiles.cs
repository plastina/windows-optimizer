using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WindowsOptimizer.Core;

/// <summary>A tweak that belongs to one game, so the UI can remove all of a game's tweaks together.</summary>
public interface IGameTweak
{
    string GamePath { get; }
}

/// <summary>The games the user added, kept in <c>games.json</c> next to the backups.</summary>
public sealed class GameLibrary
{
    private readonly string _file;

    public GameLibrary(string dataRoot) => _file = Path.Combine(dataRoot, "games.json");

    public IReadOnlyList<string> Games()
    {
        try
        {
            return File.Exists(_file)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file)) ?? new List<string>()
                : new List<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<string>();
        }
    }

    /// <summary>Adds a game's .exe. Returns an error message, or null on success.</summary>
    public string? Add(string exePath)
    {
        var error = Validate(exePath);
        if (error is not null) return error;

        var full = Path.GetFullPath(exePath);
        var games = Games().ToList();
        if (games.Contains(full, StringComparer.OrdinalIgnoreCase)) return "This game is already in the list.";

        games.Add(full);
        Save(games);
        return null;
    }

    public void Remove(string exePath)
    {
        var games = Games().Where(g => !string.Equals(g, exePath, StringComparison.OrdinalIgnoreCase)).ToList();
        Save(games);
    }

    /// <summary>The checks that keep the Defender exclusion (and the rest) away from folders that must stay scanned.</summary>
    public static string? Validate(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "Pick the game's .exe file.";
        if (!File.Exists(exePath)) return "That file does not exist.";

        var folder = Path.GetDirectoryName(Path.GetFullPath(exePath));
        if (folder is null || Path.GetPathRoot(folder) == folder)
            return "The game can't sit in the root of a drive: its folder would be the whole drive.";

        var protectedFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (protectedFolders.Any(p => p.Length > 0 && string.Equals(folder, p, StringComparison.OrdinalIgnoreCase)))
            return "That folder is shared with other programs (Windows, Program Files, your profile or Downloads), so it can't be used for a game.";

        return null;
    }

    private void Save(List<string> games)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Registry values for one game (the GPU preference).</summary>
public sealed class GameRegistryTweak : RegistryTweak, IGameTweak
{
    public GameRegistryTweak(
        TweakContext ctx, string gamePath, string id, string name, string description, RiskLevel risk, params RegistryEdit[] edits)
        : base(ctx, id, name, description, TweakCategory.GameProfiles, risk, edits)
    {
        GamePath = gamePath;
    }

    public string GamePath { get; }
}

/// <summary>
/// Adds a compatibility flag (for example "turn off fullscreen optimizations") to one game, keeping the flags
/// Windows or the user already set for it. Undo puts the exact previous value back, or removes the value.
/// </summary>
public sealed class AppCompatFlagTweak : TweakBase, IGameTweak
{
    private const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private readonly string _flag;

    public AppCompatFlagTweak(TweakContext ctx, string gamePath, string id, string name, string description, string flag)
        : base(ctx, id, name, description, TweakCategory.GameProfiles, RiskLevel.Low)
    {
        GamePath = gamePath;
        _flag = flag;
    }

    public string GamePath { get; }

    private string Current() =>
        Ctx.Registry.GetValue(RegHive.CurrentUser, Layers, GamePath) is { Kind: RegKind.String } v ? (string)v.Data : "";

    private bool Has(string flags) => flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(_flag, StringComparer.OrdinalIgnoreCase);

    public override TweakStatus GetStatus() => Has(Current()) ? TweakStatus.Applied : TweakStatus.NotApplied;

    public override string? Fingerprint() => Current();

    public override IReadOnlyList<PreviewLine> Preview() =>
        new[] { new PreviewLine("Compatibility flags", Current() is { Length: > 0 } now ? now : "(none)", WithFlag(Current()), GamePath) };

    public override TweakResult Apply()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null)
        {
            snapshot = new TweakSnapshot { TweakId = Id };
            snapshot.Data["previous"] = Ctx.Registry.GetValue(RegHive.CurrentUser, Layers, GamePath) is { Kind: RegKind.String } v ? (string)v.Data : "\0absent";
            Ctx.State.Save(snapshot);
        }

        try
        {
            Ctx.Registry.SetValue(RegHive.CurrentUser, Layers, GamePath, RegValue.Str(WithFlag(Current())));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Ctx.State.Delete(Id);
            return TweakResult.Fail($"Could not write the compatibility flags: {ex.Message}");
        }

        return TweakResult.Ok("Done. Start the game again for it to take effect.");
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null || !snapshot.Data.TryGetValue("previous", out var previous))
            return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        if (previous == "\0absent")
        {
            Ctx.Registry.DeleteValue(RegHive.CurrentUser, Layers, GamePath);
            Ctx.Registry.DeleteKeyIfEmpty(RegHive.CurrentUser, Layers);
        }
        else
        {
            Ctx.Registry.SetValue(RegHive.CurrentUser, Layers, GamePath, RegValue.Str(previous));
        }

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Restored.");
    }

    /// <summary>Windows stores the flags as "~ FLAG1 FLAG2"; the leading "~" is part of the format.</summary>
    private string WithFlag(string flags)
    {
        if (Has(flags)) return flags;
        var trimmed = flags.Trim();
        if (trimmed.Length == 0) return "~ " + _flag;
        return (trimmed.StartsWith('~') ? trimmed : "~ " + trimmed) + " " + _flag;
    }
}

/// <summary>
/// Tells Windows Defender not to scan a game's folder. It is a real trade-off (malware dropped in that folder
/// would go unnoticed), so it is Medium risk and warns before running. Defender itself stays on.
/// </summary>
public sealed class DefenderExclusionTweak : TweakBase, IGameTweak
{
    private const string ExclusionPaths = @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths";

    public DefenderExclusionTweak(TweakContext ctx, string gamePath, string id, string gameName)
        : base(ctx, id, $"Don't scan the {gameName} folder with Defender",
            $"Adds the folder of {gameName} to the Windows Defender exclusions, so real-time scanning stops reading every game file as it loads. Defender stays on for everything else.",
            TweakCategory.GameProfiles, RiskLevel.Medium)
    {
        GamePath = gamePath;
        NeedsAdmin = true;
        Warning = "Defender won't scan this folder, so malware placed in it would go unnoticed. Use this only for a game folder you trust and keep the game updated from its official source.";
    }

    public string GamePath { get; }

    private string Folder => Path.GetDirectoryName(GamePath)!;

    public override TweakStatus GetStatus() =>
        Ctx.Registry.GetValueNames(RegHive.LocalMachine, ExclusionPaths).Contains(Folder, StringComparer.OrdinalIgnoreCase)
            ? TweakStatus.Applied
            : TweakStatus.NotApplied;

    public override IReadOnlyList<PreviewLine> Preview() =>
        new[] { new PreviewLine("Defender exclusion", GetStatus() == TweakStatus.Applied ? "excluded" : "scanned", "excluded", Folder) };

    public override TweakResult Apply()
    {
        // A folder that was already excluded isn't ours to remove later, so it gets no backup.
        if (GetStatus() == TweakStatus.Applied) return TweakResult.Ok("That folder was already excluded.");

        Ctx.State.Save(new TweakSnapshot { TweakId = Id });
        var result = Ctx.Runner.RunPowerShell($"Add-MpPreference -ExclusionPath '{Folder.Replace("'", "''")}'");
        if (!result.Success)
        {
            Ctx.State.Delete(Id);
            return TweakResult.Fail("Defender refused the change (Tamper Protection or a policy may block it): " + result.Text);
        }
        return TweakResult.Ok("Folder excluded from real-time scanning.");
    }

    public override TweakResult Undo()
    {
        if (Ctx.State.Load(Id) is null) return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        var result = Ctx.Runner.RunPowerShell($"Remove-MpPreference -ExclusionPath '{Folder.Replace("'", "''")}'");
        if (!result.Success) return TweakResult.Fail(result.Text);

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Folder is scanned again.");
    }
}

/// <summary>Builds the tweaks of one game.</summary>
public static class GameProfileTweaks
{
    public const string GpuPreferenceKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public static IReadOnlyList<ITweak> For(TweakContext c, string gamePath)
    {
        var game = Path.GetFileNameWithoutExtension(gamePath);
        var slug = Slug(game);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(gamePath.ToLowerInvariant())))[..6].ToLowerInvariant();
        var id = $"game-{slug}-{hash}";

        return new ITweak[]
        {
            new GameRegistryTweak(c, gamePath, id + "-gpu", $"Use the high-performance GPU for {game}",
                $"Tells Windows to run {game} on the strongest graphics card. It matters on laptops and PCs with two GPUs, where games sometimes start on the integrated one.",
                RiskLevel.Low,
                Reg.CuString(GpuPreferenceKey, gamePath, "GpuPreference=2;")),
            new AppCompatFlagTweak(c, gamePath, id + "-fullscreen", $"Turn off fullscreen optimizations for {game}",
                $"Makes Windows run {game} in true exclusive fullscreen instead of its borderless-window mode. It can lower input delay in some games and change how alt-tab behaves.",
                "DISABLEDXMAXIMIZEDWINDOWEDMODE"),
            new DefenderExclusionTweak(c, gamePath, id + "-defender", game),
        };
    }

    private static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) slug.Append(ch);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var result = slug.ToString().Trim('-');
        return result.Length == 0 ? "game" : result;
    }
}
