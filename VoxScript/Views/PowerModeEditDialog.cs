// VoxScript/Views/PowerModeEditDialog.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoxScript.Core.AI;
using VoxScript.Core.PowerMode;

namespace VoxScript.Views;

public static class PowerModeEditDialog
{
    private static readonly string[] PresetNames = ["Formal", "Semi-casual", "Casual", "Custom"];

    /// <summary>
    /// Shows an edit dialog for a Power Mode config. Returns the edited config, or null if cancelled.
    /// If the user clicks Delete, returns a config with Id = -1 as a sentinel.
    /// </summary>
    public static async Task<PowerModeConfig?> ShowAsync(XamlRoot xamlRoot, PowerModeConfig? existing)
    {
        var isNew = existing is null;
        var config = existing ?? new PowerModeConfig { IsEnabled = true, Preset = EnhancementPreset.SemiCasual };

        var nameBox = new TextBox
        {
            Text = config.Name,
            PlaceholderText = "Mode name",
            FontSize = 14,
            Header = "Name",
        };

        var presetCombo = new ComboBox
        {
            ItemsSource = PresetNames,
            SelectedIndex = config.Preset switch
            {
                EnhancementPreset.Formal => 0,
                EnhancementPreset.SemiCasual => 1,
                EnhancementPreset.Casual => 2,
                _ => 3,
            },
            Header = "Style",
            Width = 200,
        };

        var promptBox = new TextBox
        {
            Text = config.SystemPrompt ?? "",
            PlaceholderText = "Custom system prompt for this mode",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            FontSize = 13,
            Header = "System prompt",
            Visibility = config.Preset == EnhancementPreset.Custom ? Visibility.Visible : Visibility.Collapsed,
        };

        presetCombo.SelectionChanged += (_, _) =>
        {
            promptBox.Visibility = presetCombo.SelectedIndex == 3
                ? Visibility.Visible : Visibility.Collapsed;
        };

        var appsBox = new TextBox
        {
            Text = config.ProcessNameFilter ?? "",
            PlaceholderText = "WhatsApp, Discord, Telegram",
            FontSize = 14,
            Header = "Apps (comma-separated process names)",
        };

        var urlBox = new TextBox
        {
            Text = config.UrlPatternFilter ?? "",
            PlaceholderText = @"mail\.google\.com (optional regex)",
            FontSize = 14,
            Header = "URL pattern (optional)",
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 450 };
        panel.Children.Add(nameBox);
        panel.Children.Add(presetCombo);
        panel.Children.Add(promptBox);
        panel.Children.Add(appsBox);
        panel.Children.Add(urlBox);

        // Delete/Reset button for existing configs
        if (!isNew)
        {
            var deleteBtn = new Button
            {
                Content = config.IsBuiltIn ? "Reset to defaults" : "Delete this mode",
                Foreground = config.IsBuiltIn
                    ? GetBrush("BrandMutedBrush")
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0),
            };
            deleteBtn.Click += async (_, _) =>
            {
                if (config.IsBuiltIn) return; // reset handled separately
                if (await Helpers.DialogHelper.ConfirmDeleteAsync(xamlRoot, $"mode \"{config.Name}\""))
                {
                    // Signal deletion with sentinel
                    config.Id = -1;
                }
            };
            if (!config.IsBuiltIn)
                panel.Children.Add(deleteBtn);
        }

        var dialog = new ContentDialog
        {
            Title = isNew ? "Add Context Mode" : "Edit Context Mode",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            CornerRadius = new CornerRadius(12),
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return null;

            config.Name = name;
            config.Preset = presetCombo.SelectedIndex switch
            {
                0 => EnhancementPreset.Formal,
                1 => EnhancementPreset.SemiCasual,
                2 => EnhancementPreset.Casual,
                _ => EnhancementPreset.Custom,
            };
            config.SystemPrompt = config.Preset == EnhancementPreset.Custom ? promptBox.Text.Trim() : null;
            config.ProcessNameFilter = appsBox.Text.Trim();
            config.UrlPatternFilter = string.IsNullOrWhiteSpace(urlBox.Text) ? null : urlBox.Text.Trim();
            return config;
        }

        return config.Id == -1 ? config : null; // -1 = delete sentinel
    }

    private static SolidColorBrush GetBrush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
