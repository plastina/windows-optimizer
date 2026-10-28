using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WindowsOptimizer.App;

/// <summary>
/// Swaps the palette at run time. The XAML only refers to brushes through DynamicResource, so replacing the
/// entries in the application resources restyles every open window at once.
/// </summary>
public static class ThemeManager
{
    // Light: cool workshop greys. Dark: blue-grey (not black), with the state colours lifted to keep their contrast.
    private static readonly Dictionary<string, string> Light = new()
    {
        ["PaperBrush"] = "#E9ECEF", ["SurfaceBrush"] = "#FFFFFF", ["SheetHeadBrush"] = "#F2F4F6",
        ["InkBrush"] = "#15202B", ["InkHoverBrush"] = "#2C3B4A", ["OnInkBrush"] = "#FFFFFF",
        ["MutedBrush"] = "#566370", ["LineBrush"] = "#D3D9DF", ["HoverBrush"] = "#DCE1E6",
        ["AppliedBrush"] = "#1E8E66", ["AppliedTextBrush"] = "#14694A",
        ["PartialBrush"] = "#D69E2E", ["PartialTextBrush"] = "#8A5A00", ["DangerTextBrush"] = "#B83A1B",
        ["FocusBrush"] = "#2F6FED", ["DisabledBgBrush"] = "#D3D9DF", ["DisabledFgBrush"] = "#6F7B86",
        ["BannerBgBrush"] = "#FFF4D6", ["BannerLineBrush"] = "#E3C469", ["ScrollThumbBrush"] = "#B5BEC7",
        ["InfoTextBrush"] = "#2456B8",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["PaperBrush"] = "#1A2129", ["SurfaceBrush"] = "#232D38", ["SheetHeadBrush"] = "#2A3541",
        ["InkBrush"] = "#E6EBF0", ["InkHoverBrush"] = "#FFFFFF", ["OnInkBrush"] = "#1A2129",
        ["MutedBrush"] = "#A0ADBA", ["LineBrush"] = "#38454F", ["HoverBrush"] = "#2E3A46",
        ["AppliedBrush"] = "#3DBA8C", ["AppliedTextBrush"] = "#62D3A8",
        ["PartialBrush"] = "#D8A53A", ["PartialTextBrush"] = "#E3B95A", ["DangerTextBrush"] = "#FF9276",
        ["FocusBrush"] = "#6FA0FF", ["DisabledBgBrush"] = "#34404B", ["DisabledFgBrush"] = "#8593A0",
        ["BannerBgBrush"] = "#3B3320", ["BannerLineBrush"] = "#8A7430", ["ScrollThumbBrush"] = "#4F5E6C",
        ["InfoTextBrush"] = "#7FA8FF",
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int UseImmersiveDarkMode = 20;

    public static bool IsDark(ThemeChoice choice) =>
        choice == ThemeChoice.Dark || (choice == ThemeChoice.System && SystemPrefersDark());

    public static void Apply(ThemeChoice choice)
    {
        var dark = IsDark(choice);
        var palette = dark ? Dark : Light;
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }

        foreach (Window window in Application.Current.Windows) UseDarkTitleBar(window, dark);
    }

    /// <summary>Gives a window the dark title bar too; the palette alone only reaches the client area.</summary>
    public static void UseDarkTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0;
    }
}
