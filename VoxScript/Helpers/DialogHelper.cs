using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VoxScript.Helpers;

public static class DialogHelper
{
    /// <summary>
    /// Show a confirmation dialog. Returns true if the user confirmed.
    /// </summary>
    public static async Task<bool> ConfirmDeleteAsync(XamlRoot xamlRoot, string itemDescription)
    {
        var deleteStyle = new Style(typeof(Button));
        deleteStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68))));
        deleteStyle.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Microsoft.UI.Colors.White)));
        deleteStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(4)));

        var cancelStyle = new Style(typeof(Button));
        cancelStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Microsoft.UI.Colors.White)));
        cancelStyle.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 46, 36))));
        cancelStyle.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200))));
        cancelStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        cancelStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(4)));

        // Swap the two-tone background: content area uses the darker shade,
        // button area (command space) uses the lighter shade
        var dialogStyle = new Style(typeof(ContentDialog));
        dialogStyle.Setters.Add(new Setter(ContentDialog.BackgroundProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 243))));

        var dialog = new ContentDialog
        {
            Title = "Delete",
            Content = $"Are you sure you want to delete {itemDescription}?",
            PrimaryButtonText = "Delete",
            PrimaryButtonStyle = deleteStyle,
            CloseButtonText = "Cancel",
            CloseButtonStyle = cancelStyle,
            Style = dialogStyle,
            CornerRadius = new CornerRadius(12),
            XamlRoot = xamlRoot,
        };

        // Override the command space background (bottom half where buttons sit)
        dialog.Resources["ContentDialogCommandSpaceBackground"] =
            new SolidColorBrush(Microsoft.UI.Colors.White);

        // Overlay color set globally in AppColors.xaml

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
