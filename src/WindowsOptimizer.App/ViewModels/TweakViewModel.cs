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

    public TweakViewModel(ITweak tweak, MainViewModel main)
    {
        _tweak = tweak;
        _main = main;
        ApplyCommand = new AsyncCommand(ApplyAsync);
        UndoCommand = new AsyncCommand(UndoAsync);
    }

    public ITweak Tweak => _tweak;
    public string Id => _tweak.Id;
    public string Name => _tweak.Name;
    public string Description => _tweak.Description;
    public TweakCategory Category => _tweak.Category;
    public RiskLevel Risk => _tweak.Risk;
    public bool NeedsRestart => _tweak.NeedsRestart;
    public string? Warning => _tweak.Warning;
    public bool HasWarning => !string.IsNullOrEmpty(_tweak.Warning);

    public string RiskText => Risk switch
    {
        RiskLevel.High => "Risco alto",
        RiskLevel.Medium => "Risco médio",
        _ => "Risco baixo",
    };

    public bool IsOneShot => _tweak.Kind == TweakKind.OneShot;
    public string ApplyLabel => IsOneShot ? "Executar" : "Aplicar";

    /// <summary>Undo only appears when there is a backup to go back to, so rows don't carry a dead button.</summary>
    public bool ShowUndo => _canUndo;

    public TweakStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value)) Raise(nameof(StatusText), nameof(HasStatus), nameof(CanApply), nameof(NoBackupNote));
        }
    }

    public string StatusText => Status switch
    {
        TweakStatus.Applied => "Aplicado",
        TweakStatus.Partial => "Aplicado em parte",
        TweakStatus.NotApplied => "Não aplicado",
        _ => "",
    };

    public bool HasStatus => Status != TweakStatus.Unknown;

    /// <summary>Applied by something else (an earlier script, a policy): there is no backup, so Undo can't exist.</summary>
    public bool NoBackupNote => !IsOneShot && Status == TweakStatus.Applied && !_canUndo;

    public bool CanApply => !IsBusy && (IsOneShot || Status != TweakStatus.Applied);
    public bool CanUndoNow => !IsBusy && _canUndo;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) Raise(nameof(CanApply), nameof(CanUndoNow));
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
    public ICommand UndoCommand { get; }

    /// <summary>Reads the current state. Safe to call from a background thread.</summary>
    public (TweakStatus Status, bool CanUndo) Probe()
    {
        try
        {
            return (_tweak.GetStatus(), _tweak.CanUndo());
        }
        catch (Exception ex)
        {
            _main.Engine.Write($"Não foi possível ler o estado de '{Name}': {ex.Message}");
            return (TweakStatus.Unknown, false);
        }
    }

    public void ApplyProbe((TweakStatus Status, bool CanUndo) probe)
    {
        Status = probe.Status;
        _canUndo = probe.CanUndo;
        Raise(nameof(CanUndoNow), nameof(ShowUndo), nameof(NoBackupNote));
        _main.RefreshCounts();
    }

    private async Task ApplyAsync()
    {
        if (HasWarning &&
            MessageBox.Show($"{Warning}\n\nContinuar?", Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _main.EnsureRestorePointAsync(_tweak);
            var result = await Task.Run(() => _main.Engine.Apply(_tweak));
            Show(result);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UndoAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _main.Engine.Undo(_tweak));
            Show(result);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
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
            _main.Engine.Write($"Não foi possível listar as alterações de '{Name}': {ex.Message}");
            lines = Array.Empty<PreviewLine>();
        }

        Preview.Clear();
        foreach (var line in lines) Preview.Add(line);
        _previewLoaded = true;
        Raise(nameof(PreviewEmpty));
    }

    private void Show(TweakResult result)
    {
        ResultIsError = !result.Success;
        ResultText = result.Message.Length > 0
            ? result.Message
            : result.Success ? "Pronto." : "Falhou.";
    }
}
