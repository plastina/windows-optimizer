using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App.ViewModels;

public sealed class TweakViewModel : ObservableObject
{
    private readonly ITweak _tweak;
    private readonly MainViewModel _main;

    private TweakStatus _status = TweakStatus.Unknown;
    private bool _canUndo;
    private bool _isBusy;
    private bool _isExpanded;
    private bool _previewLoaded;
    private string _resultText = "";
    private bool _resultIsError;

    /// <param name="unavailable">The tweak can't be used on this PC (an app that isn't installed...): it is listed, greyed out, with the reason.</param>
    public TweakViewModel(ITweak tweak, MainViewModel main, bool unavailable = false)
    {
        _tweak = tweak;
        _main = main;
        IsUnavailable = unavailable;
        ApplyCommand = new AsyncCommand(ApplyAsync);
    }

    public ITweak Tweak => _tweak;

    /// <summary>For the tweaks of a game from "Your games": the .exe they belong to.</summary>
    public string? GamePath => (_tweak as IGameTweak)?.GamePath;
    public bool IsGameTweak => _tweak is IGameTweak;
    public ICommand RemoveGameCommand => new AsyncCommand(() => _main.RemoveGameAsync(GamePath!));
    public string Id => _tweak.Id;
    public string Name => _tweak.Name;
    public string Description => _tweak.Description;
    public TweakCategory Category => _tweak.Category;
    public string CategoryTitle => MainViewModel.CategoryText[Category].Title;
    public RiskLevel Risk => _tweak.Risk;
    public string? Warning => _tweak.Warning;
    public bool HasWarning => !string.IsNullOrEmpty(_tweak.Warning);

    // Badges next to the name.
    public bool NeedsRestart => _tweak.NeedsRestart;
    public bool RestartsExplorer => _tweak.RestartsExplorer;
    public bool RequiresAdmin => _tweak.RequiresAdmin;
    public bool IsRecommended => _tweak.Recommended && !IsUnavailable;
    public bool IsExperimental => _tweak.Experimental;
    public bool IsSlow => _tweak.SlowOperation;
    public bool ShowRiskChip => Risk != RiskLevel.Low;
    public bool IsHighRisk => Risk == RiskLevel.High;
    public bool IsMediumRisk => Risk == RiskLevel.Medium;

    public string RiskText => Risk switch
    {
        RiskLevel.High => "High risk",
        RiskLevel.Medium => "Medium risk",
        _ => "Low risk",
    };

    public bool IsUnavailable { get; private set; }

    /// <summary>The tweak turned out not to apply to this PC (Windows doesn't have the component): list it with its reason instead.</summary>
    public void MarkUnavailable()
    {
        IsUnavailable = true;
        Raise(nameof(IsUnavailable), nameof(ShowSwitch), nameof(ShowRun), nameof(CanToggle), nameof(CanRun), nameof(IsRecommended), nameof(NoBackupNote));
    }
    public string UnavailableReason => _tweak.UnavailableReason ?? "Not available on this PC.";

    public bool IsOneShot => _tweak.Kind == TweakKind.OneShot;

    /// <summary>Toggles get a switch; actions (clean, create, remove...) get a Run button.</summary>
    public bool ShowSwitch => !IsOneShot && !IsUnavailable;
    public bool ShowRun => IsOneShot && !IsUnavailable;
    public string ApplyLabel => "Run";

    public TweakStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
                Raise(nameof(StateText), nameof(HasStatus), nameof(IsPartial), nameof(IsOn), nameof(CanToggle), nameof(NoBackupNote));
        }
    }

    public bool HasStatus => Status != TweakStatus.Unknown;
    public bool IsPartial => Status == TweakStatus.Partial;

    /// <summary>The words next to the switch.</summary>
    public string StateText => Status switch
    {
        TweakStatus.Applied => "On",
        TweakStatus.Partial => "Partly on",
        TweakStatus.NotApplied => "Off",
        _ => "",
    };

    /// <summary>
    /// What the switch shows. Setting it applies (on) or undoes (off) the tweak, then the switch snaps to the real
    /// state, so a failed or cancelled change doesn't leave it lying.
    /// </summary>
    public bool IsOn
    {
        get => Status == TweakStatus.Applied;
        set
        {
            if (IsBusy || value == IsOn) return;
            _ = RunSafelyAsync(value ? ApplyAsync : UndoAsync);
        }
    }

    /// <summary>Applied by something else (an earlier script, a policy): there is no backup, so it can't be switched off here.</summary>
    public bool NoBackupNote => ShowSwitch && IsOn && !_canUndo;

    public bool CanToggle => !IsBusy && ShowSwitch && (!IsOn || _canUndo);
    public bool CanRun => !IsBusy && ShowRun;
    public bool CanUndoNow => !IsBusy && _canUndo;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) Raise(nameof(CanToggle), nameof(CanRun), nameof(CanUndoNow));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value) && value) _ = LoadPreviewAsync();
        }
    }

    public ObservableCollection<PreviewLine> Preview { get; } = new();
    public bool PreviewEmpty => _previewLoaded && Preview.Count == 0;

    public string ResultText
    {
        get => _resultText;
        private set
        {
            if (Set(ref _resultText, value)) Raise(nameof(HasResult));
        }
    }

    public bool ResultIsError
    {
        get => _resultIsError;
        private set => Set(ref _resultIsError, value);
    }

    public bool HasResult => ResultText.Length > 0;

    public ICommand ApplyCommand { get; }

    /// <summary>Reads the current state. Safe to call from a background thread.</summary>
    public (TweakStatus Status, bool CanUndo) Probe()
    {
        try
        {
            return (_tweak.GetStatus(), _tweak.CanUndo());
        }
        catch (Exception ex)
        {
            _main.Engine.Write($"Could not read the state of '{Name}': {ex.Message}");
            return (TweakStatus.Unknown, false);
        }
    }

    public void ApplyProbe((TweakStatus Status, bool CanUndo) probe)
    {
        Status = probe.Status;
        _canUndo = probe.CanUndo;
        Raise(nameof(CanToggle), nameof(CanUndoNow), nameof(NoBackupNote), nameof(IsOn));
        _main.RefreshCounts();
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Show(TweakResult.Fail(ex.Message));
        }
        finally
        {
            Raise(nameof(IsOn));
        }
    }

    private async Task ApplyAsync()
    {
        if (HasWarning &&
            MessageBox.Show($"{Warning}\n\nContinue?", Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            != MessageBoxResult.Yes)
        {
            Raise(nameof(IsOn));
            return;
        }

        IsBusy = true;
        try
        {
            await _main.EnsureRestorePointAsync(_tweak);
            var result = await Task.Run(() => _main.Engine.Apply(_tweak));
            Show(result);
            if (result.Success && RestartsExplorer) _main.NoteExplorerRestartNeeded();
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
            Raise(nameof(IsOn));
        }
    }

    private async Task UndoAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _main.Engine.Undo(_tweak));
            Show(result);
            if (result.Success && RestartsExplorer) _main.NoteExplorerRestartNeeded();
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
            Raise(nameof(IsOn));
        }
    }

    private async Task RefreshAsync()
    {
        ApplyProbe(await Task.Run(Probe));
        if (IsExpanded) await LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        IReadOnlyList<PreviewLine> lines;
        try
        {
            lines = await Task.Run(() => _tweak.Preview());
        }
        catch (Exception ex)
        {
            _main.Engine.Write($"Could not list the changes of '{Name}': {ex.Message}");
            lines = Array.Empty<PreviewLine>();
        }

        Preview.Clear();
        foreach (var line in lines) Preview.Add(line.Item.Length == 0 ? line with { Item = "(Default)" } : line);
        _previewLoaded = true;
        Raise(nameof(PreviewEmpty));
    }

    private void Show(TweakResult result)
    {
        ResultIsError = !result.Success;
        ResultText = result.Message.Length > 0
            ? result.Message
            : result.Success ? "Done." : "Failed.";
    }
}
