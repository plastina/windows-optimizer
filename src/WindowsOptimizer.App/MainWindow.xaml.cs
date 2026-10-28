using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsOptimizer.App.ViewModels;

namespace WindowsOptimizer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (App.ScreenshotPath is not null)
        {
            // Render off-screen so the capture doesn't flash on the user's desktop.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000;
            ShowInTaskbar = false;
        }
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();

        if (App.ScreenshotPath is { } path)
        {
            await CaptureAsync(path);
            Application.Current.Shutdown();
            return;
        }

        // Last, so a slow or missing network never delays the first screen.
        if (!App.DemoUpdate) await ViewModel.CheckForUpdateAsync();
    }

    private void OnLogChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();

    private async Task CaptureAsync(string path)
    {
        var categories = ViewModel.Categories;
        ViewModel.Selected = categories[Math.Clamp(App.ScreenshotCategory, 0, categories.Count - 1)];
        if (App.ScreenshotExpand)
        {
            foreach (var tweak in ViewModel.Selected.Tweaks.Take(2)) tweak.IsExpanded = true;
        }

        // Let bindings, layout and the async preview load settle before drawing. The health page reads
        // the hardware first, which can take a few seconds.
        await Task.Delay(800);
        for (var i = 0; i < 100 && ViewModel.Health.IsRunning; i++) await Task.Delay(200);
        await Task.Delay(300);
        UpdateLayout();

        var content = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
