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

        for (var i = 0; i < e.Args.Length; i++)
        {
            switch (e.Args[i])
            {
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

        var window = new MainWindow { DataContext = new MainViewModel(TweakEngine.CreateDefault()) };
        window.Show();
    }
}
