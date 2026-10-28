using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WindowsOptimizer.App;

/// <summary>Shows an element when the bound value is false (the built-in converter shows it when true).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
