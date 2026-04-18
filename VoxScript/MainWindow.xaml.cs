// VoxScript/MainWindow.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoxScript.Infrastructure;
using VoxScript.Views;
using WinUIEx;

namespace VoxScript.Shell;

public sealed partial class MainWindow : Window
{
    private const int MinWindowWidth = 900;
    private const int MinWindowHeight = 600;

    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        this.Closed += OnClosed;

        // Set window icon (absolute path so it works regardless of working directory)
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

        // Enforce minimum window size
        AppWindow.Changed += (s, e) =>
        {
            if (e.DidSizeChange)
            {
                var size = AppWindow.Size;
                bool changed = false;
                int w = size.Width, h = size.Height;
                if (w < MinWindowWidth) { w = MinWindowWidth; changed = true; }
                if (h < MinWindowHeight) { h = MinWindowHeight; changed = true; }
                if (changed) AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            }
        };

        // Style caption buttons (min/max/close) for light background
        if (AppWindow?.TitleBar is { } titleBar)
        {
            titleBar.ButtonForegroundColor = Colors.Black;
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Colors.Gray;
        }

        // Navigate to transcribe page by default and mark Home as selected
        ContentFrame.Navigate(typeof(TranscribePage));
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            Type? pageType = tag switch
            {
                "Home"        => typeof(TranscribePage),
                "Dictionary"  => typeof(DictionaryPage),
                "Expansions"  => typeof(ExpansionsPage),
                "History"     => typeof(HistoryPage),
                "Personalize" => typeof(PersonalizePage),
                "Notes"       => typeof(NotesPage),
                "Settings"    => typeof(SettingsPage),
                _             => typeof(TranscribePage),
            };
            ContentFrame.Navigate(pageType);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        var settings = ServiceLocator.Get<VoxScript.Core.Settings.AppSettings>();
        if (settings.MinimizeToTray)
        {
            args.Handled = true;
            this.Hide();
        }
        else
        {
            // App.ExitApp guarantees process termination via Environment.Exit;
            // Application.Current.Exit() can stall when background threads
            // (keyboard hook, audio capture, native whisper, etc.) are alive.
            App.ExitApp();
        }
    }

    public void BringToFront()
    {
        this.Show();
        this.Activate();
    }

    public void NavigateTo(Type pageType)
    {
        ContentFrame.Navigate(pageType);

        // Update sidebar selection to match
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            var tag = item.Tag?.ToString();
            Type? mapped = tag switch
            {
                "Home"        => typeof(TranscribePage),
                "Dictionary"  => typeof(DictionaryPage),
                "Expansions"  => typeof(ExpansionsPage),
                "History"     => typeof(HistoryPage),
                "Personalize" => typeof(PersonalizePage),
                "Notes"       => typeof(NotesPage),
                "Settings"    => typeof(SettingsPage),
                _             => null,
            };
            if (mapped == pageType)
            {
                NavView.SelectedItem = item;
                break;
            }
        }
    }
}
