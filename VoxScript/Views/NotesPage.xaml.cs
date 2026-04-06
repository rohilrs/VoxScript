using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class NotesPage : Page
{
    public static NotesViewModel SharedViewModel { get; } = new();

    private DispatcherTimer? _searchDebounce;

    public NotesPage()
    {
        this.InitializeComponent();
        SharedViewModel.Notes.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(RebuildList);
        BuildSortPills();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await SharedViewModel.LoadAsync();
        RebuildList();
        UpdateVisibility();
    }

    // ── New Note ────────────────────────────────────────────────

    private async void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var item = await SharedViewModel.CreateAsync();
        NoteEditorManager.OpenEditor(item.Id);
    }

    // ── Search ──────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SharedViewModel.SearchQuery = SearchBox.Text;
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce?.Stop();
            await SharedViewModel.SearchAsync();
            RebuildList();
            UpdateVisibility();
        };
        _searchDebounce.Start();
    }

    // ── Sort Pills ──────────────────────────────────────────────

    private void BuildSortPills()
    {
        SortPanel.Children.Clear();
        var sorts = new[] { ("Newest", SortMode.Newest), ("Oldest", SortMode.Oldest), ("A\u2013Z", SortMode.Alphabetical) };
        foreach (var (label, mode) in sorts)
        {
            var pill = new Button
            {
                Content = label,
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(12),
                Background = mode == SharedViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = mode == SharedViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"]
                    : (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                BorderThickness = new Thickness(0),
            };
            pill.Click += (_, _) =>
            {
                SharedViewModel.CurrentSort = mode;
                BuildSortPills();
                RebuildList();
            };
            SortPanel.Children.Add(pill);
        }
    }

    // ── Card List ───────────────────────────────────────────────

    private void UpdateVisibility()
    {
        EmptyState.Visibility = SharedViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildList()
    {
        NotesList.Children.Clear();
        foreach (var item in SharedViewModel.Notes)
            NotesList.Children.Add(BuildCard(item));
        UpdateVisibility();
    }

    private Border BuildCard(NoteItem item)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["BrandCardBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            BorderThickness = new Thickness(1),
        };

        var outer = new StackPanel { Spacing = 6 };

        // Header: title + copy button
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 14, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            Content = copyIcon,
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(copyBtn, "Copy to clipboard");
        copyBtn.Click += async (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(item.ContentPlainText);
            Clipboard.SetContent(dp);
            copyIcon.Glyph = "\uE73E";
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"];
            await Task.Delay(1500);
            copyIcon.Glyph = "\uE8C8";
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
        };
        Grid.SetColumn(copyBtn, 1);
        header.Children.Add(copyBtn);

        outer.Children.Add(header);

        // Preview text
        if (!string.IsNullOrWhiteSpace(item.Preview))
        {
            outer.Children.Add(new TextBlock
            {
                Text = item.Preview,
                FontSize = 13,
                LineHeight = 20,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            });
        }

        // Footer: timestamp + badge
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(new TextBlock
        {
            Text = item.TimeDisplay,
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            Opacity = 0.7,
        });

        if (item.IsStarred)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "\u2605 Saved",
                FontSize = 10,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 212, 168, 83)),
            });
        }
        else
        {
            footer.Children.Add(new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock
                {
                    Text = "Note",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                },
            });
        }
        outer.Children.Add(footer);

        card.Child = outer;

        // Click card → open editor
        card.PointerPressed += (_, _) => NoteEditorManager.OpenEditor(item.Id);

        return card;
    }
}

/// <summary>
/// Manages the singleton NoteEditorWindow. Call OpenEditor to show/focus it.
/// </summary>
public static class NoteEditorManager
{
    private static Shell.NoteEditorWindow? _window;

    public static void OpenEditor(int? selectNoteId = null)
    {
        if (_window is null)
        {
            _window = new Shell.NoteEditorWindow();
            _window.Closed += (_, _) => _window = null;
        }

        _window.Activate();

        if (selectNoteId.HasValue)
            NotesPage.SharedViewModel.SelectById(selectNoteId.Value);
    }
}
