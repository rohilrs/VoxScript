using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VoxScript.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VoxScript.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    private Button? _activeRecordingButton;

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        this.InitializeComponent();
    }

    // ── Keybind button click handlers ──────────────────────────

    private void HoldHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("HoldHotkey", (Button)sender);

    private void ToggleHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("ToggleHotkey", (Button)sender);

    private void PasteLastHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("PasteLastHotkey", (Button)sender);

    private void CancelHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("CancelHotkey", (Button)sender);

    private void ResetKeybindsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetKeybindsToDefaults();
    }

    private async void SetApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var keyBox = new PasswordBox
        {
            PlaceholderText = "Enter API key",
            Width = 400,
        };
        var dialog = new ContentDialog
        {
            Title = "Set API Key",
            Content = keyBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.SaveApiKey(keyBox.Password);
        }
    }

    private void ClearApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearApiKey();
    }

    private async void SetStructuralApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var keyBox = new PasswordBox
        {
            PlaceholderText = "Enter API key",
            Width = 400,
        };
        var dialog = new ContentDialog
        {
            Title = "Set Structural Formatting API Key",
            Content = keyBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.SaveStructuralApiKey(keyBox.Password);
        }
    }

    private void ClearStructuralApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearStructuralApiKey();
    }

    private async void EditStructuralPromptButton_Click(object sender, RoutedEventArgs e)
    {
        // ContentDialog's default ContentDialogMaxWidth theme resource is 456 DIPs —
        // anything wider than that gets clipped on the right, which was what made
        // text like "The text has" appear cut off mid-word regardless of our
        // TextBox MaxWidth. We override the resource on this dialog instance below.
        // MinHeight is kept well below the 600-DIP window minimum so the dialog
        // always fits with breathing room.
        var promptBox = new TextBox
        {
            Text = ViewModel.GetEffectiveStructuralPrompt(),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 240,
            MaxHeight = 520,
            MinWidth = 600,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(promptBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(promptBox, ScrollBarVisibility.Disabled);

        var panel = new StackPanel { MinWidth = 600, MaxWidth = 640 };
        panel.Children.Add(promptBox);

        var dialog = new ContentDialog
        {
            Title = "Edit Structural Formatting Prompt",
            Content = panel,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Reset to default",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        // Widen the dialog beyond the default 456 DIP theme max so the 640 DIP
        // content area actually fits without horizontal clipping.
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;

        // Secondary button resets the textbox to the built-in default without closing.
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            promptBox.Text = VoxScript.Core.AI.StructuralFormattingPrompt.System;
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // If the user saved text identical to the default, clear the override
            // so future default-prompt updates are picked up automatically.
            if (promptBox.Text.Trim() == VoxScript.Core.AI.StructuralFormattingPrompt.System.Trim())
                ViewModel.ResetStructuralPromptToDefault();
            else
                ViewModel.SaveStructuralPromptOverride(promptBox.Text);
        }
    }

    private async void ManageModelsButton_Click(object sender, RoutedEventArgs e)
    {
        await ModelManagementDialog.ShowAsync(this.XamlRoot);
        ViewModel.RefreshCurrentModel();
    }

    private static SolidColorBrush GetBrush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    private void StartKeybindRecording(string slot, Button button)
    {
        // Reset previous button style if any
        ResetButtonStyle();

        // Highlight the active button
        _activeRecordingButton = button;
        button.Background = GetBrush("BrandPrimaryBrush");
        button.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        button.BorderBrush = GetBrush("BrandPrimaryBrush");

        ViewModel.StartRecordingKeybind(slot);
        this.Focus(FocusState.Programmatic);

        // Listen for when recording ends to reset style
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsRecordingKeybind) && !ViewModel.IsRecordingKeybind)
        {
            ResetButtonStyle();
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void ResetButtonStyle()
    {
        if (_activeRecordingButton is null) return;
        _activeRecordingButton.Background = GetBrush("BrandBackgroundBrush");
        _activeRecordingButton.Foreground = GetBrush("BrandForegroundBrush");
        _activeRecordingButton.BorderBrush = GetBrush("BrandPrimaryLightBrush");
        _activeRecordingButton = null;
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", [".json"]);
        picker.SuggestedFileName = "voxscript-data";

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        using var stream = await file.OpenStreamForWriteAsync();
        stream.SetLength(0);
        await ViewModel.ExportDataAsync(stream, default);

        DataPortInfoBar.Message = ViewModel.DataPortStatusMessage;
        DataPortInfoBar.Severity = ViewModel.DataPortIsError
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Success;
        DataPortInfoBar.IsOpen = true;
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        using var stream = await file.OpenStreamForReadAsync();
        await ViewModel.ImportDataAsync(stream, default);

        DataPortInfoBar.Message = ViewModel.DataPortStatusMessage;
        DataPortInfoBar.Severity = ViewModel.DataPortIsError
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Success;
        DataPortInfoBar.IsOpen = true;
    }

    // ── Key event forwarding to ViewModel ──────────────────────

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.HandleKeyDown((int)e.Key))
        {
            e.Handled = true;
        }
    }

    private void Page_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.HandleKeyUp((int)e.Key))
        {
            e.Handled = true;
        }
    }
}
