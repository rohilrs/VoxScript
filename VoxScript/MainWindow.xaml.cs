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
    // Device-independent pixels (WinUIEx.WindowManager units).
    private const int MinWindowWidth = 975;
    private const int MinWindowHeight = 600;

    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        this.Closed += OnClosed;

        // Set window icon (absolute path so it works regardless of working directory)
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));

        // Enforce minimum window size through WinUIEx's WindowManager, which
        // hooks WM_GETMINMAXINFO under the hood so the OS itself caps the drag.
        // The reactive AppWindow.Changed + Resize approach caused visible
        // flashing because the window was briefly drawn at the under-sized
        // dimensions before being snapped back.
        var manager = WindowManager.Get(this);
        manager.MinWidth = MinWindowWidth;
        manager.MinHeight = MinWindowHeight;

        // Style caption buttons (min/max/close) for light background
        if (AppWindow?.TitleBar is { } titleBar)
        {
            titleBar.ButtonForegroundColor = Colors.Black;
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Colors.Gray;
        }

        // Navigate to transcribe page by default and mark Home as selected
        ContentFrame.Navigate(typeof(HomePage));
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
                "Home"        => typeof(HomePage),
                "Dictionary"  => typeof(DictionaryPage),
                "Expansions"  => typeof(ExpansionsPage),
                "History"     => typeof(HistoryPage),
                "Personalize" => typeof(PersonalizePage),
                "Notes"       => typeof(NotesPage),
                "Settings"    => typeof(SettingsPage),
                _             => typeof(HomePage),
            };
            ContentFrame.Navigate(pageType);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        var settings = ServiceLocator.Get<VoxScript.Core.Settings.AppSettings>();

        // X-out during onboarding always exits fully — users haven't seen the tray
        // yet and quietly hiding feels like the app vanished into the background.
        if (settings.OnboardingCompleted != true)
        {
            App.ExitApp();
            return;
        }

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

    /// <summary>
    /// Replace the normal shell (NavigationView) with the onboarding wizard view.
    /// Called on first launch before the Activate happens.
    /// </summary>
    public void ShowOnboarding(UIElement onboardingView)
    {
        OnboardingPresenter.Content = onboardingView;
        OnboardingPresenter.Visibility = Visibility.Visible;
        NavView.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Swap back to the normal shell after onboarding completes.
    /// </summary>
    public void ShowShell()
    {
        OnboardingPresenter.Visibility = Visibility.Collapsed;
        OnboardingPresenter.Content = null;
        NavView.Visibility = Visibility.Visible;
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
                "Home"        => typeof(HomePage),
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
