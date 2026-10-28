namespace WindowsOptimizer.Core;

public enum TweakCategory { Updates, Power, Gaming, GameProfiles, Appearance, Language, Network, Startup, AI, Privacy, System }

public enum RiskLevel { Low, Medium, High }

/// <summary>Toggle tweaks can be undone; one-shot tweaks are actions you run.</summary>
public enum TweakKind { Toggle, OneShot }

public enum TweakStatus { Unknown, NotApplied, Partial, Applied }

public readonly record struct TweakResult(bool Success, string Message)
{
    public static TweakResult Ok(string message = "") => new(true, message);
    public static TweakResult Fail(string message) => new(false, message);
}

/// <summary>One row of the "what changes" sheet: an item, its current value and the value after applying.</summary>
public sealed record PreviewLine(string Item, string Current, string Target, string? Detail = null);

public interface ITweak
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    TweakCategory Category { get; }
    RiskLevel Risk { get; }
    TweakKind Kind { get; }
    bool NeedsRestart { get; }

    /// <summary>Changing it needs administrator rights (HKLM values, services, system commands).</summary>
    bool RequiresAdmin { get; }

    /// <summary>The change only shows after File Explorer (the taskbar and desktop shell) restarts.</summary>
    bool RestartsExplorer { get; }

    /// <summary>Worth turning on for most people: low risk and a clear benefit.</summary>
    bool Recommended { get; }

    /// <summary>Not widely tested or documented: it may do nothing on some builds of Windows.</summary>
    bool Experimental { get; }

    /// <summary>Running it can take minutes (it installs or removes a Windows component).</summary>
    bool SlowOperation { get; }

    /// <summary>Why <see cref="IsAvailable"/> is false (an app that isn't installed, hardware that is missing...).</summary>
    string? UnavailableReason { get; }

    /// <summary>Shown before running when the tweak is risky or cannot be undone.</summary>
    string? Warning { get; }

    bool IsAvailable();
    TweakStatus GetStatus();

    /// <summary>
    /// A digest of the real system state this tweak changes, read straight from Windows (not from what the
    /// tweak last wrote). The self-test compares it before, after applying and after undoing. Null when the
    /// status already reads the real state.
    /// </summary>
    string? Fingerprint();

    bool CanUndo();
    IReadOnlyList<PreviewLine> Preview();
    TweakResult Apply();
    TweakResult Undo();
}
