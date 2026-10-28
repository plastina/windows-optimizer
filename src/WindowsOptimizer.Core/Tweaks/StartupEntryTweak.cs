using System.Security.Cryptography;
using System.Text;

namespace WindowsOptimizer.Core;

/// <summary>
/// One program that starts with Windows. Applying the tweak disables it the same way Task Manager does:
/// by writing a "disabled" flag under StartupApproved. The entry itself is never deleted, so Undo is exact.
/// </summary>
public sealed class StartupEntryTweak : TweakBase
{
    private readonly RegHive _hive;
    private readonly string _approvedKey;
    private readonly string _valueName;
    private readonly string _display;
    private readonly string _command;

    public StartupEntryTweak(
        TweakContext ctx, RegHive hive, string approvedKey, string valueName, string display, string command, string origin)
        : base(ctx, MakeId(hive, approvedKey, valueName), $"Don't start {display} at sign-in",
            $"Stops {display} from starting with Windows. The program stays installed and you can still open it yourself. Command: {command}",
            TweakCategory.Startup, RiskLevel.Low)
    {
        _hive = hive;
        _approvedKey = approvedKey;
        _valueName = valueName;
        _display = display;
        _command = command;
        Origin = origin;
        NeedsAdmin = hive == RegHive.LocalMachine;
    }

    /// <summary>Where the entry comes from, e.g. "Current user" or "All users (Startup folder)".</summary>
    public string Origin { get; }

    /// <summary>Applied means "disabled at sign-in".</summary>
    public override TweakStatus GetStatus() => IsDisabled() ? TweakStatus.Applied : TweakStatus.NotApplied;

    public override string? Fingerprint() =>
        Ctx.Registry.GetBinary(_hive, _approvedKey, _valueName) is { } data ? Convert.ToHexString(data) : "(no flag)";

    public override IReadOnlyList<PreviewLine> Preview() => new[]
    {
        new PreviewLine("Starts with Windows", IsDisabled() ? "no" : "yes", "no", $"{Origin}: {_command}"),
    };

    public override TweakResult Apply()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null)
        {
            snapshot = new TweakSnapshot { TweakId = Id };
            snapshot.Data["previous"] = Ctx.Registry.GetBinary(_hive, _approvedKey, _valueName) is { } old
                ? Convert.ToBase64String(old)
                : "";
            Ctx.State.Save(snapshot);
        }

        try
        {
            Ctx.Registry.SetBinary(_hive, _approvedKey, _valueName, DisabledFlag());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Ctx.State.Delete(Id);
            return TweakResult.Fail($"Could not write {_approvedKey}: {ex.Message}");
        }

        return TweakResult.Ok($"{_display} will no longer start with Windows.");
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null || !snapshot.Data.TryGetValue("previous", out var previous))
            return TweakResult.Fail("No backup for this tweak; the app did not apply it.");

        if (previous.Length == 0)
            Ctx.Registry.DeleteValue(_hive, _approvedKey, _valueName);
        else
            Ctx.Registry.SetBinary(_hive, _approvedKey, _valueName, Convert.FromBase64String(previous));

        Ctx.State.Delete(Id);
        return TweakResult.Ok($"{_display} will start with Windows again.");
    }

    // StartupApproved values are 12 bytes: the first byte's lowest bit means "disabled", the rest is a FILETIME.
    private bool IsDisabled() =>
        Ctx.Registry.GetBinary(_hive, _approvedKey, _valueName) is { Length: > 0 } data && (data[0] & 1) == 1;

    private static byte[] DisabledFlag()
    {
        var data = new byte[12];
        data[0] = 0x03;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        return data;
    }

    private static string MakeId(RegHive hive, string approvedKey, string valueName)
    {
        var slug = new StringBuilder();
        foreach (var ch in Path.GetFileNameWithoutExtension(valueName).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) slug.Append(ch);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        // A short hash keeps ids unique when two sources hold an entry with the same name.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{hive}|{approvedKey}|{valueName}")))[..6].ToLowerInvariant();
        return $"startup-{slug.ToString().Trim('-')}-{hash}".Replace("startup--", "startup-");
    }
}

/// <summary>Finds the programs that start with Windows: Run keys and the two Startup folders.</summary>
public static class StartupDiscovery
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static IReadOnlyList<ITweak> Discover(TweakContext c, bool includeFolders = true)
    {
        var found = new List<ITweak>();

        void FromRunKey(RegHive hive, string runKey, string approvedKey, string origin)
        {
            foreach (var name in c.Registry.GetValueNames(hive, runKey).Where(n => n.Length > 0))
            {
                var command = c.Registry.GetValue(hive, runKey, name) is { Kind: RegKind.String } text ? (string)text.Data : "";
                found.Add(new StartupEntryTweak(c, hive, approvedKey, name, name, command, origin));
            }
        }

        void FromFolder(string folder, RegHive hive, string origin)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var fileName = Path.GetFileName(file);
                found.Add(new StartupEntryTweak(
                    c, hive, Approved + @"\StartupFolder", fileName, Path.GetFileNameWithoutExtension(fileName), file, origin));
            }
        }

        FromRunKey(RegHive.CurrentUser, Run, Approved + @"\Run", "Current user");
        FromRunKey(RegHive.LocalMachine, Run, Approved + @"\Run", "All users");
        FromRunKey(RegHive.LocalMachine, Run32, Approved + @"\Run32", "All users (32-bit)");
        if (includeFolders)
        {
            FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegHive.CurrentUser, "Current user (Startup folder)");
            FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegHive.LocalMachine, "All users (Startup folder)");
        }

        return found
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
