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
            }
        }

        if (selfTestPath is not null)
        {
            Shutdown(RunSelfTest(selfTestPath));
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel(TweakEngine.CreateDefault()) };
        window.Show();
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
