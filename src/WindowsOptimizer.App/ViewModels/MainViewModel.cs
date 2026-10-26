using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Input;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App.ViewModels;

public sealed class CategoryViewModel : ObservableObject
{
    public CategoryViewModel(string title, string blurb, IReadOnlyList<TweakViewModel> tweaks)
    {
        Title = title;
        Blurb = blurb;
        Tweaks = tweaks;
    }

    public string Title { get; }
    public string Blurb { get; }
    public IReadOnlyList<TweakViewModel> Tweaks { get; }

    /// <summary>"3/5": how many of the tweaks that report a state are applied. Empty when none do.</summary>
    public string CountText
    {
        get
        {
            var tracked = Tweaks.Count(t => t.HasStatus);
            return tracked == 0 ? "" : $"{Tweaks.Count(t => t.Status == TweakStatus.Applied)}/{tracked}";
        }
    }

    public void RefreshCount() => Raise(nameof(CountText));
}

public sealed class MainViewModel : ObservableObject
{
    private const string RestorePointId = "restore-point";

    private static readonly Dictionary<TweakCategory, (string Title, string Blurb)> CategoryText = new()
    {
        [TweakCategory.Updates] = ("Updates", "Control when and what Windows Update installs."),
        [TweakCategory.Power] = ("Power", "Steady performance, without the PC turning off the screen or sleeping mid-game."),
        [TweakCategory.Gaming] = ("Gaming", "Priority for the game and less running behind it."),
        [TweakCategory.Appearance] = ("Appearance", "Theme, date and time, taskbar and Start menu."),
        [TweakCategory.Language] = ("Language and region", "Display language, region and keyboard."),
        [TweakCategory.Network] = ("Network", "DNS servers of the network adapters."),
        [TweakCategory.Privacy] = ("Cleanup and privacy", "Fewer suggestions, less history and fewer apps you didn't ask for."),
        [TweakCategory.System] = ("System", "Restore point, disk maintenance and sign-in."),
    };

    private readonly StringBuilder _log = new();
    private readonly ITweak? _restorePoint;
    private bool _restorePointDone;
    private CategoryViewModel? _selected;
    private bool _isLoading = true;
    private bool _showLog;
    private bool _createRestorePoint = true;
    private string _lastMessage = "Reading the current Windows state…";
    private string _logText = "";

    public MainViewModel(TweakEngine engine)
    {
        Engine = engine;
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Engine.Logged += OnLogged;

        _restorePoint = engine.Tweaks.FirstOrDefault(t => t.Id == RestorePointId);

        AllTweaks = engine.Tweaks
            .Where(t => t.IsAvailable())
            .Select(t => new TweakViewModel(t, this))
            .ToList();

        var everything = new CategoryViewModel("All tweaks", "Everything the app can change, in category order.", AllTweaks);
        Categories.Add(everything);
        foreach (var group in AllTweaks.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            var (title, blurb) = CategoryText[group.Key];
            Categories.Add(new CategoryViewModel(title, blurb, group.ToList()));
        }

        _selected = Categories[0];
        RefreshCommand = new AsyncCommand(LoadAsync);
        OpenLogFolderCommand = new AsyncCommand(() =>
        {
            Directory.CreateDirectory(TweakEngine.DataRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TweakEngine.DataRoot}\"") { UseShellExecute = true });
            return Task.CompletedTask;
        });
    }

    public TweakEngine Engine { get; }
    public IReadOnlyList<TweakViewModel> AllTweaks { get; }
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();
    public bool IsAdmin { get; }
    public bool ShowAdminBanner => !IsAdmin;

    public ICommand RefreshCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public CategoryViewModel? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public bool ShowLog
    {
        get => _showLog;
        set => Set(ref _showLog, value);
    }

    public bool CreateRestorePoint
    {
        get => _createRestorePoint;
        set => Set(ref _createRestorePoint, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        private set => Set(ref _lastMessage, value);
    }

    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LastMessage = "Reading the current Windows state…";

        var probes = await Task.Run(() => AllTweaks.Select(t => (Tweak: t, Probe: t.Probe())).ToList());
        foreach (var (tweak, probe) in probes) tweak.ApplyProbe(probe);

        RefreshCounts();
        IsLoading = false;
        LastMessage = $"{AllTweaks.Count} tweaks available.";
    }

    public void RefreshCounts()
    {
        foreach (var category in Categories) category.RefreshCount();
    }

    /// <summary>Creates a restore point once per session, before the first change, if the user wants one.</summary>
    public async Task EnsureRestorePointAsync(ITweak about)
    {
        if (!CreateRestorePoint || _restorePointDone || _restorePoint is null || about.Id == RestorePointId) return;
        _restorePointDone = true;

        LastMessage = "Creating a restore point before the first tweak…";
        var result = await Task.Run(() => Engine.Apply(_restorePoint));
        if (!result.Success)
            LastMessage = "Could not create the restore point; the tweak continues.";
    }

    private void OnLogged(string line)
    {
        // Tweaks run on worker threads; the log and the status line live on the UI thread.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _log.AppendLine(line);
            LogText = _log.ToString();
            LastMessage = line[(line.IndexOf("  ", StringComparison.Ordinal) + 2)..];
        });
    }
}
