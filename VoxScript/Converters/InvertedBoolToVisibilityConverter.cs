using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace VoxScript.Converters;

/// <summary>
/// Converts bool to Visibility: true → Collapsed, false → Visible.
/// </summary>
public sealed class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Collapsed;
}
