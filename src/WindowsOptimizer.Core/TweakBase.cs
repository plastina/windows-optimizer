namespace WindowsOptimizer.Core;

/// <summary>Everything a tweak may touch. Tests swap these for fakes.</summary>
public sealed record TweakContext(IRegistryAccess Registry, StateStore State, ICommandRunner Runner, string DataRoot);

public abstract class TweakBase : ITweak
{
    protected TweakBase(
        TweakContext ctx, string id, string name, string description, TweakCategory category, RiskLevel risk)
    {
        Ctx = ctx;
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        Risk = risk;
    }

    protected TweakContext Ctx { get; }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public TweakCategory Category { get; }
    public RiskLevel Risk { get; }
    public TweakKind Kind { get; init; } = TweakKind.Toggle;
    public bool NeedsRestart { get; init; }
    public string? Warning { get; init; }

    /// <summary>Set for tweaks that run commands or services; registry tweaks work it out from their edits.</summary>
    public bool NeedsAdmin { get; init; }
    public virtual bool RequiresAdmin => NeedsAdmin;
    public bool RestartsExplorer { get; init; }
    public bool Recommended { get; set; }
    public bool Experimental { get; init; }
    public bool SlowOperation { get; init; }
    public string? UnavailableReason { get; set; }

    public virtual bool IsAvailable() => true;
    public abstract TweakStatus GetStatus();
    public virtual string? Fingerprint() => null;
    public virtual bool CanUndo() => Ctx.State.Exists(Id);
    public virtual IReadOnlyList<PreviewLine> Preview() => Array.Empty<PreviewLine>();
    public abstract TweakResult Apply();
    public virtual TweakResult Undo() => TweakResult.Fail("This tweak can't be undone by the app.");
}
