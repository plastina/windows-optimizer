using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App.ViewModels;

public sealed class CategoryViewModel : ObservableObject
{
    private ICollectionView? _grouped;

    public CategoryViewModel(string title, string blurb, ObservableCollection<TweakViewModel> tweaks, PageKind kind = PageKind.Tweaks, string group = "")
    {
        Title = title;
        Blurb = blurb;
        Tweaks = tweaks;
        Kind = kind;
        Group = group;
        Tweaks.CollectionChanged += (_, _) => Raise(nameof(ShowsEmptyGames), nameof(CountText), nameof(SummaryText));
    }

    public string Title { get; }

    /// <summary>The heading this page sits under in the sidebar. Empty for "All tweaks", which has none.</summary>
    public string Group { get; }

    /// <summary>"All tweaks" lists every category, so its tweaks come under a heading per category.</summary>
    public bool GroupByCategory { get; init; }

    // Each list is bound only on the page that uses it, so a page never builds its rows twice.
    public ObservableCollection<TweakViewModel>? FlatTweaks => GroupByCategory ? null : Tweaks;

    public ICollectionView? GroupedTweaks
    {
        get
        {
            if (!GroupByCategory) return null;
            if (_grouped is null)
            {
                _grouped = new CollectionViewSource { Source = Tweaks }.View;
                _grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TweakViewModel.CategoryTitle)));
            }
            return _grouped;
        }
    }
    public string Blurb { get; }
    public ObservableCollection<TweakViewModel> Tweaks { get; }
    public PageKind Kind { get; }

    /// <summary>Tweaks that can't be used on this PC, listed under "Not available on this PC" with the reason.</summary>
    public ObservableCollection<TweakViewModel> Unavailable { get; } = new();
    public bool HasUnavailable => Unavailable.Count > 0;

    /// <summary>The AI apps installed for this user (only on the "AI and Copilot" page).</summary>
    public ObservableCollection<AiAppViewModel> AiApps { get; } = new();
    public bool ShowsAiApps { get; init; }
    public bool NoAiApps => ShowsAiApps && AiApps.Count == 0;

    public void NotifyListsChanged() => Raise(nameof(HasUnavailable), nameof(NoAiApps));

    public bool ShowsTweaks => Kind is PageKind.Tweaks or PageKind.Games;
    public bool IsHealth => Kind == PageKind.Health;
    public bool IsMeasure => Kind == PageKind.Measure;
    public bool IsGames => Kind == PageKind.Games;
    public bool ShowsEmptyGames => IsGames && Tweaks.Count == 0;

    /// <summary>"3/5": how many of the tweaks that report a state are applied. Empty when none do.</summary>
    public string CountText
    {
        get
        {
            var tracked = Tweaks.Count(t => t.HasStatus);
            return tracked == 0 ? "" : $"{Tweaks.Count(t => t.Status == TweakStatus.Applied)}/{tracked}";
        }
    }

    /// <summary>"10 of 15 on", for the page header. Empty when no tweak reports a state.</summary>
    public string SummaryText
    {
        get
        {
            var tracked = Tweaks.Count(t => t.HasStatus);
            return tracked == 0 ? "" : $"{Tweaks.Count(t => t.Status == TweakStatus.Applied)} of {tracked} on";
        }
    }

    public void RefreshCount() => Raise(nameof(CountText), nameof(SummaryText));
}

public sealed class MainViewModel : ObservableObject
{
    private const string RestorePointId = "restore-point";

    /// <summary>The order of the pages in the sidebar, and the heading each sits under.</summary>
    private static readonly (TweakCategory Category, string Group)[] Layout =
    {
        (TweakCategory.Gaming, "Performance"),
        (TweakCategory.GameProfiles, "Performance"),
        (TweakCategory.Power, "Performance"),
        (TweakCategory.Network, "Performance"),
        (TweakCategory.Startup, "Performance"),
        (TweakCategory.Updates, "Windows"),
        (TweakCategory.Appearance, "Windows"),
        (TweakCategory.Language, "Windows"),
        (TweakCategory.System, "Windows"),
        (TweakCategory.AI, "Privacy"),
        (TweakCategory.Privacy, "Privacy"),
    };

    private static int LayoutIndex(TweakCategory category) => Array.FindIndex(Layout, l => l.Category == category);

    internal static readonly Dictionary<TweakCategory, (string Title, string Blurb)> CategoryText = new()
    {
        [TweakCategory.Updates] = ("Updates", "Control when and what Windows Update installs."),
        [TweakCategory.Power] = ("Power", "Steady performance, without the PC turning off the screen or sleeping mid-game."),
        [TweakCategory.Gaming] = ("Gaming", "Priority for the game and less running behind it."),
        [TweakCategory.GameProfiles] = ("Your games", "Settings for one game at a time. Add a game's .exe and choose what to change for it."),
        [TweakCategory.Appearance] = ("Appearance", "Theme, date and time, taskbar and Start menu."),
        [TweakCategory.Language] = ("Language and region", "Display language, region and keyboard."),
        [TweakCategory.Network] = ("Network", "DNS servers of the network adapters."),
        [TweakCategory.AI] = ("AI and Copilot", "Turn off the AI features Windows puts in the system and its apps, and remove the AI apps that came with it."),
        [TweakCategory.Startup] = ("Startup programs", "Apps that start with Windows. Turning one off keeps it installed; you can still open it yourself."),
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
    private readonly AppSettings _settings;
    private readonly GameLibrary _games;
    private bool _hasUpdate;
    private string _updateText = "";
    private string _updateUrl = UpdateChecker.ReleasesPage;
    private bool _updateDownloaded;
    private string _updateButtonText = "Download";

    public MainViewModel(TweakEngine engine, AppSettings settings)
    {
        Engine = engine;
        _settings = settings;
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Engine.Logged += OnLogged;

        _restorePoint = engine.Tweaks.FirstOrDefault(t => t.Id == RestorePointId);

        // Ask once whether each tweak applies to this PC; the ones that don't are kept to be listed with their reason.
        var availability = engine.Tweaks.Select(t => (Tweak: t, Available: t.IsAvailable())).ToList();
        // In sidebar order, so "All tweaks" reads category by category.
        AllTweaks = new ObservableCollection<TweakViewModel>(
            availability.Where(a => a.Available).Select(a => new TweakViewModel(a.Tweak, this)).OrderBy(t => LayoutIndex(t.Category)));
        var unavailableTweaks = availability.Where(a => !a.Available && a.Tweak.UnavailableReason is not null)
            .Select(a => new TweakViewModel(a.Tweak, this, unavailable: true))
            .ToList();

        var context = engine.Context ?? throw new InvalidOperationException("The engine has no context.");
        Health = new HealthViewModel(context);
        Measure = new MeasureViewModel(context);
        _games = new GameLibrary(context.DataRoot);

        Categories.Add(new CategoryViewModel("All tweaks", "Everything the app can change, grouped by category.", AllTweaks) { GroupByCategory = true });
        Categories.Add(new CategoryViewModel("PC health check",
            "Looks for what costs the most performance and no tweak can fix: refresh rate, memory speed, drivers, drives.", new(), PageKind.Health, "Check"));
        Categories.Add(new CategoryViewModel("Before and after",
            "Take a snapshot, apply tweaks, take another and see what really changed.", new(), PageKind.Measure, "Check"));

        // "Your games" is always listed, even when empty, so there is a place to add one.
        foreach (var (category, group) in Layout)
        {
            var tweaks = new ObservableCollection<TweakViewModel>(AllTweaks.Where(t => t.Category == category));
            if (tweaks.Count == 0 && category != TweakCategory.GameProfiles) continue;

            var (title, blurb) = CategoryText[category];
            var page = new CategoryViewModel(title, blurb, tweaks, category == TweakCategory.GameProfiles ? PageKind.Games : PageKind.Tweaks, group)
            {
                ShowsAiApps = category == TweakCategory.AI,
            };
            foreach (var tweak in unavailableTweaks.Where(t => t.Category == category)) page.Unavailable.Add(tweak);
            Categories.Add(page);
        }

        // A category whose tweaks are all unavailable (say, only an app that isn't installed) would be skipped above.
        foreach (var group in unavailableTweaks.GroupBy(t => t.Category).Where(g => Categories.All(c => c.ShowsTweaks == false || c.Title != CategoryText[g.Key].Title)))
        {
            var (title, blurb) = CategoryText[group.Key];
            var page = new CategoryViewModel(title, blurb, new ObservableCollection<TweakViewModel>(), PageKind.Tweaks, Layout[LayoutIndex(group.Key)].Group);
            foreach (var tweak in group) page.Unavailable.Add(tweak);
            Categories.Add(page);
        }

        // The sidebar lists the pages under their headings.
        CategoriesView = CollectionViewSource.GetDefaultView(Categories);
        CategoriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CategoryViewModel.Group)));

        RestartExplorerCommand = new AsyncCommand(RestartExplorerAsync);
        DismissExplorerCommand = new AsyncCommand(() =>
        {
            ExplorerRestartNeeded = false;
            return Task.CompletedTask;
        });

        _selected = Categories[0];
        AddGameCommand = new AsyncCommand(AddGameAsync);
        CycleThemeCommand = new AsyncCommand(() =>
        {
            // System -> Light -> Dark -> System
            _settings.Theme = (ThemeChoice)(((int)_settings.Theme + 1) % 3);
            _settings.Save();
            ThemeManager.Apply(_settings.Theme);
            Raise(nameof(ThemeLabel));
            return Task.CompletedTask;
        });
        OpenUpdateCommand = new AsyncCommand(() =>
        {
            if (_updateDownloaded) UpdateChecker.RestartToUpdate();
            else Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        DismissUpdateCommand = new AsyncCommand(() =>
        {
            HasUpdate = false;
            return Task.CompletedTask;
        });
        RefreshCommand = new AsyncCommand(LoadAsync);
        OpenLogFolderCommand = new AsyncCommand(() =>
        {
            Directory.CreateDirectory(TweakEngine.DataRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TweakEngine.DataRoot}\"") { UseShellExecute = true });
            return Task.CompletedTask;
        });
    }

    public TweakEngine Engine { get; }
    public ObservableCollection<TweakViewModel> AllTweaks { get; }
    public HealthViewModel Health { get; }
    public MeasureViewModel Measure { get; }
    public ICommand AddGameCommand { get; }
    public ICommand RestartExplorerCommand { get; private set; } = null!;
    public ICommand DismissExplorerCommand { get; private set; } = null!;

    private bool _explorerRestartNeeded;

    /// <summary>A tweak was changed that only shows after File Explorer restarts.</summary>
    public bool ExplorerRestartNeeded
    {
        get => _explorerRestartNeeded;
        private set => Set(ref _explorerRestartNeeded, value);
    }

    public void NoteExplorerRestartNeeded() => ExplorerRestartNeeded = true;

    /// <summary>
    /// Restarts the taskbar and desktop. It asks first (open File Explorer windows close), kills explorer.exe, and
    /// lets Windows bring the shell back by itself; if that doesn't happen, it starts it as a normal (not elevated) user.
    /// </summary>
    private async Task RestartExplorerAsync()
    {
        if (MessageBox.Show("File Explorer windows will close and the taskbar will blink for a moment. Restart Explorer now?",
                "Restart Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await Task.Run(() =>
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try { process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }

            // Windows normally relaunches the shell on its own within a few seconds.
            for (var i = 0; i < 20 && Process.GetProcessesByName("explorer").Length == 0; i++) Thread.Sleep(300);

            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                // "trustlevel 0x20000" starts it without this app's administrator rights, as the shell should run.
                Process.Start(new ProcessStartInfo("runas.exe", "/trustlevel:0x20000 explorer.exe") { UseShellExecute = false, CreateNoWindow = true });
            }
        });

        ExplorerRestartNeeded = false;
        LastMessage = "Explorer restarted.";
    }

    /// <summary>Looks up the AI apps installed for this user (a PowerShell call, so off the UI thread).</summary>
    public async Task LoadAiAppsAsync()
    {
        var page = Categories.FirstOrDefault(c => c.ShowsAiApps);
        if (page is null) return;

        var apps = await Task.Run(() => InstalledAiApps.List(Engine.Context!));
        page.AiApps.Clear();
        foreach (var app in apps) page.AiApps.Add(new AiAppViewModel(app, this));
        page.NotifyListsChanged();
    }

    public async Task RemoveAiAppAsync(AiApp app)
    {
        if (MessageBox.Show($"Remove {app.DisplayName}? You can install it again from the Microsoft Store.",
                "Remove app", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        LastMessage = $"Removing {app.DisplayName}…";
        var result = await Task.Run(() => InstalledAiApps.Remove(Engine.Context!, app));
        Engine.Write($"{(result.Success ? "OK" : "Failed")}: remove {app.DisplayName} — {result.Message}");
        if (!result.Success) MessageBox.Show(result.Message, "Remove app", MessageBoxButton.OK, MessageBoxImage.Warning);
        await LoadAiAppsAsync();
    }
    public ObservableCollection<CategoryViewModel> Categories { get; } = new();
    public ICollectionView CategoriesView { get; }
    public bool IsAdmin { get; }
    public bool ShowAdminBanner => !IsAdmin;

    public ICommand RefreshCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand CycleThemeCommand { get; }
    public ICommand OpenUpdateCommand { get; }
    public ICommand DismissUpdateCommand { get; }

    public string ThemeLabel => $"Theme: {_settings.Theme}";

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value) return;
            _settings.CheckForUpdates = value;
            _settings.Save();
            Raise(nameof(CheckForUpdates));
        }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set => Set(ref _hasUpdate, value);
    }

    public string UpdateText
    {
        get => _updateText;
        private set => Set(ref _updateText, value);
    }

    public void ShowUpdate(ReleaseInfo release)
    {
        _updateUrl = release.Url;
        _updateDownloaded = release.Downloaded;
        UpdateText = release.Downloaded
            ? $"Version {release.Version} is downloaded (you have {UpdateChecker.CurrentVersion}). It installs when you close the app, or restart now."
            : $"Version {release.Version} is available (you have {UpdateChecker.CurrentVersion}).";
        UpdateButtonText = release.Downloaded ? "Restart now" : "Download";
        HasUpdate = true;
    }

    public string UpdateButtonText
    {
        get => _updateButtonText;
        private set => Set(ref _updateButtonText, value);
    }

    /// <summary>Looks for a newer release, if the user allows it. Never throws and never blocks the window.</summary>
    public async Task CheckForUpdateAsync()
    {
        if (!CheckForUpdates) return;
        var release = await Task.Run(UpdateChecker.CheckAsync);
        if (release is not null) ShowUpdate(release);
    }

    public CategoryViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;

            // The first time the health page opens it checks by itself; later it waits for "Check again".
            if (value is { IsHealth: true } && !Health.HasRun) _ = Health.RunAsync();
        }
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

        // Forget what was read about installed Windows components, so a refresh sees changes made elsewhere.
        WindowsComponents.Invalidate(Engine.Context!);

        var probes = await Task.Run(() => AllTweaks.ToList().Select(t => (Tweak: t, Probe: t.Probe())).ToList());
        foreach (var (tweak, probe) in probes) tweak.ApplyProbe(probe);

        // A tweak that has a reason to be unavailable and whose state can't be read is one Windows doesn't have here
        // (a component this edition lacks): move it to "Not available on this PC" instead of showing a dead switch.
        foreach (var (tweak, probe) in probes.Where(p => p.Probe.Status == TweakStatus.Unknown && p.Tweak.Tweak.UnavailableReason is not null))
        {
            tweak.MarkUnavailable();
            AllTweaks.Remove(tweak);
            foreach (var category in Categories.Where(c => c.Tweaks.Contains(tweak)).ToList()) category.Tweaks.Remove(tweak);

            var page = Categories.FirstOrDefault(c => c.Title == CategoryText[tweak.Category].Title);
            if (page is not null && !page.Unavailable.Contains(tweak)) page.Unavailable.Add(tweak);
        }
        foreach (var category in Categories) category.NotifyListsChanged();

        RefreshCounts();
        IsLoading = false;
        LastMessage = $"{AllTweaks.Count} tweaks available.";

        await LoadAiAppsAsync();
    }

    public void RefreshCounts()
    {
        foreach (var category in Categories) category.RefreshCount();
    }

    /// <summary>Asks for a game's .exe and adds its tweaks to "Your games".</summary>
    private async Task AddGameAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick the game's .exe",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        await AddGameAsync(dialog.FileName);
    }

    public async Task AddGameAsync(string exePath)
    {
        if (_games.Add(exePath) is { } error)
        {
            MessageBox.Show(error, "Your games", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var gameCategory = Categories.First(c => c.IsGames);
        foreach (var tweak in GameProfileTweaks.For(Engine.Context!, Path.GetFullPath(exePath)))
        {
            var view = new TweakViewModel(tweak, this);
            view.ApplyProbe(await Task.Run(view.Probe));
            AllTweaks.Add(view);
            gameCategory.Tweaks.Add(view);
        }

        Selected = gameCategory;
        LastMessage = $"Added {Path.GetFileNameWithoutExtension(exePath)}. Nothing changes until you press Apply.";
    }

    /// <summary>Undoes what the app applied for a game, then forgets the game.</summary>
    public async Task RemoveGameAsync(string gamePath)
    {
        var game = Path.GetFileNameWithoutExtension(gamePath);
        var tweaks = AllTweaks.Where(t => t.Tweak is IGameTweak g && string.Equals(g.GamePath, gamePath, StringComparison.OrdinalIgnoreCase)).ToList();
        var applied = tweaks.Where(t => t.CanUndoNow).ToList();

        var question = applied.Count == 0
            ? $"Remove {game} from the list?"
            : $"Remove {game}? The {applied.Count} change(s) the app made for it will be undone first.";
        if (MessageBox.Show(question, "Your games", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var tweak in applied)
        {
            var result = await Task.Run(() => Engine.Undo(tweak.Tweak));
            if (!result.Success)
            {
                MessageBox.Show($"Could not undo \"{tweak.Name}\": {result.Message}\n\n{game} stays in the list.", "Your games");
                return;
            }
        }

        _games.Remove(gamePath);
        var gameCategory = Categories.First(c => c.IsGames);
        foreach (var tweak in tweaks)
        {
            AllTweaks.Remove(tweak);
            gameCategory.Tweaks.Remove(tweak);
        }
        RefreshCounts();
        LastMessage = $"Removed {game}.";
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
