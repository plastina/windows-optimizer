namespace WindowsOptimizer.Core;

public enum TweakCategory { Updates, Power, Gaming, Appearance, Language, Network, Privacy, System }

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

    /// <summary>Shown before running when the tweak is risky or cannot be undone.</summary>
    string? Warning { get; }

    bool IsAvailable();
    TweakStatus GetStatus();
    bool CanUndo();
    IReadOnlyList<PreviewLine> Preview();
    TweakResult Apply();
    TweakResult Undo();
}
