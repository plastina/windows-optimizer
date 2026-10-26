namespace WindowsOptimizer.Core;

/// <summary>A tweak defined by a few lambdas: handy for one-off actions that don't fit the registry model.</summary>
public sealed class DelegateTweak : TweakBase
{
    private readonly Func<TweakStatus> _status;
    private readonly Func<TweakResult> _apply;
    private readonly Func<TweakResult>? _undo;
    private readonly Func<bool>? _available;
    private readonly Func<IReadOnlyList<PreviewLine>>? _preview;
    private readonly Func<string?>? _fingerprint;

    public DelegateTweak(
        TweakContext ctx, string id, string name, string description, TweakCategory category, RiskLevel risk,
        Func<TweakStatus> status,
        Func<TweakResult> apply,
        Func<TweakResult>? undo = null,
        Func<bool>? available = null,
        Func<IReadOnlyList<PreviewLine>>? preview = null,
        Func<string?>? fingerprint = null)
        : base(ctx, id, name, description, category, risk)
    {
        _status = status;
        _apply = apply;
        _undo = undo;
        _available = available;
        _preview = preview;
        _fingerprint = fingerprint;
        Kind = undo is null ? TweakKind.OneShot : TweakKind.Toggle;
    }

    public override bool IsAvailable() => _available?.Invoke() ?? true;
    public override TweakStatus GetStatus() => _status();
    public override string? Fingerprint() => _fingerprint?.Invoke();
    public override bool CanUndo() => _undo is not null && _status() == TweakStatus.Applied;
    public override IReadOnlyList<PreviewLine> Preview() => _preview?.Invoke() ?? Array.Empty<PreviewLine>();
    public override TweakResult Apply() => _apply();
    public override TweakResult Undo() => _undo is null ? base.Undo() : _undo();
}
