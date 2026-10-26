namespace WindowsOptimizer.Core;

/// <summary>Runs tweaks one at a time, turns exceptions into results and keeps a log file.</summary>
public sealed class TweakEngine
{
    private readonly object _logLock = new();
    private readonly string? _logPath;

    public TweakEngine(IReadOnlyList<ITweak> tweaks, string? logPath = null)
    {
        Tweaks = tweaks;
        _logPath = logPath;
    }

    public IReadOnlyList<ITweak> Tweaks { get; }

    public event Action<string>? Logged;

    public static string DataRoot => StateStore.DefaultRoot;

    public static TweakEngine CreateDefault()
    {
        var root = DataRoot;
        var context = new TweakContext(
            new WindowsRegistry(),
            new StateStore(Path.Combine(root, "state")),
            new CommandRunner(),
            root);
        return new TweakEngine(TweakCatalog.Create(context), Path.Combine(root, "windows-optimizer.log"));
    }

    public TweakResult Apply(ITweak tweak) => Run(tweak, "Applying", tweak.Apply);

    public TweakResult Undo(ITweak tweak) => Run(tweak, "Undoing", tweak.Undo);

    public void Write(string message) => Log(message);

    private TweakResult Run(ITweak tweak, string verb, Func<TweakResult> action)
    {
        Log($"{verb}: {tweak.Name}");
        TweakResult result;
        try
        {
            result = action();
        }
        catch (Exception ex)
        {
            result = TweakResult.Fail(ex.Message);
        }

        Log($"{(result.Success ? "OK" : "Failed")}: {tweak.Name}" + (result.Message.Length > 0 ? $" — {result.Message}" : ""));
        return result;
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
        if (_logPath is not null)
        {
            lock (_logLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        Logged?.Invoke(line);
    }
}
