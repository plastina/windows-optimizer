using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App.ViewModels;

/// <summary>What the main area shows for a sidebar entry.</summary>
public enum PageKind { Tweaks, Health, Measure, Games }

/// <summary>An installed AI app with its Remove button.</summary>
public sealed class AiAppViewModel
{
    public AiAppViewModel(AiApp app, MainViewModel main)
    {
        App = app;
        RemoveCommand = new AsyncCommand(() => main.RemoveAiAppAsync(app));
    }

    public AiApp App { get; }
    public string DisplayName => App.DisplayName;
    public string PackageName => App.PackageName;
    public ICommand RemoveCommand { get; }
}

public sealed class HealthItemViewModel
{
    public HealthItemViewModel(HealthItem item) => Item = item;

    public HealthItem Item { get; }
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public string? Advice => Item.Advice;
    public bool HasAdvice => !string.IsNullOrEmpty(Item.Advice);
    public HealthLevel Level => Item.Level;

    public string LevelText => Item.Level switch
    {
        HealthLevel.Good => "Looks good",
        HealthLevel.Attention => "Needs attention",
        _ => "For your information",
    };
}

/// <summary>The read-only "is the hardware set up right?" page.</summary>
public sealed class HealthViewModel : ObservableObject
{
    private readonly TweakContext _context;
    private bool _isRunning;
    private bool _hasRun;
    private string _summary = "Checks the things that cost the most performance and that no tweak can fix.";

    public HealthViewModel(TweakContext context)
    {
        _context = context;
        RunCommand = new AsyncCommand(RunAsync);
    }

    public ObservableCollection<HealthItemViewModel> Items { get; } = new();
    public ICommand RunCommand { get; }
    public bool HasRun => _hasRun;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value)) Raise(nameof(CanRun));
        }
    }

    public bool CanRun => !_isRunning;

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public async Task RunAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Summary = "Checking…";
        try
        {
            var items = await Task.Run(() => new HealthCheck(_context).Run());

            Items.Clear();
            // What needs attention first, then what is good to know, then what is fine.
            foreach (var item in items.OrderBy(i => i.Level switch { HealthLevel.Attention => 0, HealthLevel.Info => 1, _ => 2 }))
                Items.Add(new HealthItemViewModel(item));

            var attention = items.Count(i => i.Level == HealthLevel.Attention);
            Summary = attention switch
            {
                0 => "Nothing needs attention.",
                1 => "1 thing needs attention.",
                _ => $"{attention} things need attention.",
            };
            _hasRun = true;
            Raise(nameof(HasRun));
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public sealed class SnapshotViewModel
{
    private readonly MeasureViewModel _owner;

    public SnapshotViewModel(SystemSnapshot snapshot, MeasureViewModel owner)
    {
        Snapshot = snapshot;
        _owner = owner;
        RemoveCommand = new AsyncCommand(() =>
        {
            _owner.Remove(this);
            return Task.CompletedTask;
        });
    }

    public SystemSnapshot Snapshot { get; }
    public string Label => Snapshot.Label;
    public string When => Snapshot.TakenAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string Summary =>
        $"CPU {Snapshot.IdleCpuPercent:0.#}% · {Snapshot.MemoryUsedMb:N0} MB · {Snapshot.Processes} processes · " +
        $"{Snapshot.StartupItemsEnabled} startup programs" + (Snapshot.BootSeconds is { } s ? $" · boot {s} s" : "");

    public string Display => $"{Label}  ({When})";
    public ICommand RemoveCommand { get; }

    public override string ToString() => Display;
}

/// <summary>Takes snapshots of the background load and boot time and compares two of them.</summary>
public sealed class MeasureViewModel : ObservableObject
{
    private static readonly TimeSpan SampleTime = TimeSpan.FromSeconds(10);

    private readonly TweakContext _context;
    private readonly SnapshotStore _store;
    private string _label = "Before tweaks";
    private bool _isCapturing;
    private double _progress;
    private string _status = "";
    private SnapshotViewModel? _before;
    private SnapshotViewModel? _after;

    public MeasureViewModel(TweakContext context)
    {
        _context = context;
        _store = new SnapshotStore(context.DataRoot);
        TakeCommand = new AsyncCommand(TakeAsync);
        Reload();
        _before = Snapshots.Count > 1 ? Snapshots[^2] : null;
        _after = Snapshots.Count > 0 ? Snapshots[^1] : null;
        Recompare();
    }

    public ObservableCollection<SnapshotViewModel> Snapshots { get; } = new();
    public ObservableCollection<ComparisonRow> Rows { get; } = new();
    public ICommand TakeCommand { get; }

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (Set(ref _isCapturing, value)) Raise(nameof(CanTake));
        }
    }

    public bool CanTake => !_isCapturing;

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public SnapshotViewModel? Before
    {
        get => _before;
        set
        {
            if (Set(ref _before, value)) Recompare();
        }
    }

    public SnapshotViewModel? After
    {
        get => _after;
        set
        {
            if (Set(ref _after, value)) Recompare();
        }
    }

    public bool HasSnapshots => Snapshots.Count > 0;
    public bool CanCompare => Rows.Count > 0;

    public async Task TakeAsync()
    {
        if (IsCapturing) return;
        IsCapturing = true;
        Progress = 0;
        Status = "Measuring for 10 seconds. Leave the PC alone and close games and browsers.";
        try
        {
            var label = string.IsNullOrWhiteSpace(Label) ? "Snapshot" : Label.Trim();
            var progress = new Progress<double>(p => Progress = p);
            var snapshot = await Measurement.CaptureAsync(_context, label, SampleTime, progress);
            _store.Add(snapshot);

            var previous = Snapshots.Count > 0 ? Snapshots[^1] : null;
            Reload();
            _before = previous is null ? null : Snapshots.FirstOrDefault(s => s.Snapshot.TakenAt == previous.Snapshot.TakenAt);
            _after = Snapshots[^1];
            Raise(nameof(Before), nameof(After));
            Recompare();

            Status = _before is null
                ? "Saved. Apply some tweaks, restart if asked, then take another snapshot to compare."
                : "Saved and compared with the previous snapshot.";
        }
        finally
        {
            IsCapturing = false;
        }
    }

    public void Remove(SnapshotViewModel snapshot)
    {
        _store.Remove(snapshot.Snapshot);
        Reload();
        if (_before == snapshot || _before is not null && !Snapshots.Any(s => s.Snapshot.TakenAt == _before.Snapshot.TakenAt)) _before = null;
        if (_after == snapshot || _after is not null && !Snapshots.Any(s => s.Snapshot.TakenAt == _after.Snapshot.TakenAt)) _after = null;
        Raise(nameof(Before), nameof(After));
        Recompare();
    }

    private void Reload()
    {
        Snapshots.Clear();
        foreach (var s in _store.All()) Snapshots.Add(new SnapshotViewModel(s, this));
        Raise(nameof(HasSnapshots));
    }

    private void Recompare()
    {
        Rows.Clear();
        if (_before is not null && _after is not null && _before != _after)
        {
            foreach (var row in Measurement.Compare(_before.Snapshot, _after.Snapshot)) Rows.Add(row);
        }
        Raise(nameof(CanCompare));
    }
}
