using System.IO;
using System.Windows;
using WindowsOptimizer.App.ViewModels;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.App;

public partial class App : Application
{
    /// <summary>Development switch: <c>--screenshot out.png [--select N] [--expand]</c> renders the window to a PNG and exits.</summary>
    public static string? ScreenshotPath { get; private set; }
    public static int ScreenshotCategory { get; private set; }
    public static bool ScreenshotExpand { get; private set; }

    /// <summary>Development switches: <c>--theme light|dark</c> forces a theme without saving it; <c>--demo-update</c> shows the update banner.</summary>
    public static ThemeChoice? ThemeOverride { get; private set; }
    public static bool DemoUpdate { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? selfTestPath = null;

        for (var i = 0; i < e.Args.Length; i++)
        {
            switch (e.Args[i])
            {
                case "--selftest" when i + 1 < e.Args.Length:
                    selfTestPath = e.Args[++i];
                    break;
                case "--screenshot" when i + 1 < e.Args.Length:
                    ScreenshotPath = e.Args[++i];
                    break;
                case "--select" when i + 1 < e.Args.Length && int.TryParse(e.Args[i + 1], out var index):
                    ScreenshotCategory = index;
                    i++;
                    break;
                case "--expand":
                    ScreenshotExpand = true;
                    break;
                case "--demo-update":
                    DemoUpdate = true;
                    break;
                case "--theme" when i + 1 < e.Args.Length && Enum.TryParse<ThemeChoice>(e.Args[i + 1], ignoreCase: true, out var theme):
                    ThemeOverride = theme;
                    i++;
                    break;
            }
        }

        if (selfTestPath is not null)
        {
            Shutdown(RunSelfTest(selfTestPath));
            return;
        }

        var settings = AppSettings.Load();
        if (ThemeOverride is { } forced) settings.Theme = forced;
        ThemeManager.Apply(settings.Theme);

        var viewModel = new MainViewModel(TweakEngine.CreateDefault(), settings);
        if (DemoUpdate) viewModel.ShowUpdate(new ReleaseInfo("9.9.9", UpdateChecker.ReleasesPage));

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        ThemeManager.UseDarkTitleBar(window, ThemeManager.IsDark(settings.Theme));
    }

    /// <summary>
    /// Applies and undoes every tweak for real and writes a report. Only run this on a disposable machine
    /// (Windows Sandbox, a VM): <c>WindowsOptimizer.exe --selftest report.md</c>. Returns the number of failures.
    /// </summary>
    private static int RunSelfTest(string reportPath)
    {
        // The auto sign-in tweak opens netplwiz, a window nobody is there to close.
        var skip = new HashSet<string> { "autologin-netplwiz" };
        var results = SelfTest.Run(TweakEngine.CreateDefault().Tweaks, skip);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        // With a BOM so Notepad and Windows PowerShell 5.1 read the accents correctly.
        File.WriteAllText(reportPath, SelfTest.ToMarkdown(results), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return results.Count(r => r.Outcome == SelfTestOutcome.Fail);
    }
}
