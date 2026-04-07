using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using VoxScript.ViewModels;
using VoxScript.Helpers;

namespace VoxScript.Shell;

public sealed partial class NoteEditorWindow : Window
{
    private NotesViewModel ViewModel => Views.NotesPage.SharedViewModel;
    private DispatcherTimer? _searchDebounce;
    private DispatcherTimer? _autoSaveTimer;
    private bool _isLoadingNote;
    private bool _isDraft; // true when showing an empty unsaved note
    private readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler _collectionHandler;
    private readonly System.ComponentModel.PropertyChangedEventHandler _propertyHandler;

    public NoteEditorWindow()
    {
        InitializeComponent();

        // Window setup
        AppWindow.Resize(new SizeInt32(1000, 650));
        AppWindow.Title = "Notes \u2014 VoxScript";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        // Style caption buttons for light background (same as MainWindow)
        if (AppWindow?.TitleBar is { } titleBar)
        {
            titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
            titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
        }

        _collectionHandler = (_, _) => DispatcherQueue.TryEnqueue(RebuildSidebar);
        _propertyHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.SelectedNote))
                DispatcherQueue.TryEnqueue(LoadSelectedNote);
        };
        ViewModel.Notes.CollectionChanged += _collectionHandler;
        ViewModel.PropertyChanged += _propertyHandler;

        Closed += (_, _) =>
        {
            ViewModel.Notes.CollectionChanged -= _collectionHandler;
            ViewModel.PropertyChanged -= _propertyHandler;
        };

        BuildToolbar();
        BuildSortPills();

        // Initial load
        DispatcherQueue.TryEnqueue(async () =>
        {
            await ViewModel.LoadAsync();
            RebuildSidebar();
            LoadSelectedNote();
        });
    }

    // ── Sidebar ─────────────────────────────────────────────────

    private async void SidebarNewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var item = await ViewModel.CreateAsync();
            RebuildSidebar();
            LoadSelectedNote();
            TitleBox.Focus(FocusState.Programmatic);
            TitleBox.SelectAll();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to create note");
        }
    }

    private void SidebarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchQuery = SidebarSearchBox.Text;
        if (_searchDebounce is null)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += async (_, _) =>
            {
                _searchDebounce!.Stop();
                await ViewModel.SearchAsync();
                RebuildSidebar();
            };
        }
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void BuildSortPills()
    {
        SidebarSortPanel.Children.Clear();
        var sorts = new[] { ("Newest", SortMode.Newest), ("Oldest", SortMode.Oldest), ("A\u2013Z", SortMode.Alphabetical) };
        foreach (var (label, mode) in sorts)
        {
            var pill = new Button
            {
                Content = label,
                FontSize = 10,
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(10),
                Background = mode == ViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = mode == ViewModel.CurrentSort
                    ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"]
                    : (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                BorderThickness = new Thickness(0),
            };
            pill.Click += (_, _) =>
            {
                ViewModel.CurrentSort = mode;
                BuildSortPills();
                RebuildSidebar();
            };
            SidebarSortPanel.Children.Add(pill);
        }
    }

    private void RebuildSidebar()
    {
        SidebarNotesList.Children.Clear();
        foreach (var item in ViewModel.Notes)
            SidebarNotesList.Children.Add(BuildSidebarCard(item));
    }

    private Border BuildSidebarCard(NoteItem item)
    {
        bool isSelected = ViewModel.SelectedNote?.Id == item.Id;

        var card = new Border
        {
            Background = isSelected
                ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
                : (SolidColorBrush)Application.Current.Resources["BrandCardBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = isSelected ? new Thickness(3, 0, 0, 0) : new Thickness(0),
            BorderBrush = isSelected
                ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        var outer = new StackPanel { Spacing = 3 };

        // Top row: title + copy button
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = item.Title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        Grid.SetColumn(titleText, 0);
        topRow.Children.Add(titleText);

        var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 11, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(4, 2, 4, 2),
            CornerRadius = new CornerRadius(4),
            Content = copyIcon,
            BorderThickness = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
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
        topRow.Children.Add(copyBtn);

        outer.Children.Add(topRow);

        // Preview
        if (!string.IsNullOrWhiteSpace(item.Preview))
        {
            outer.Children.Add(new TextBlock
            {
                Text = item.Preview,
                FontSize = 9,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                Opacity = 0.8,
            });
        }

        // Footer: time + badge
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        footer.Children.Add(new TextBlock
        {
            Text = item.TimeDisplay,
            FontSize = 9,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            Opacity = 0.6,
        });

        if (item.IsStarred)
        {
            footer.Children.Add(new TextBlock
            {
                Text = "\u2605",
                FontSize = 9,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 212, 168, 83)),
            });
        }
        outer.Children.Add(footer);

        card.Child = outer;

        // Click to select (Tapped doesn't propagate from Button clicks)
        card.Tapped += (_, _) =>
        {
            ViewModel.SelectedNote = item;
            RebuildSidebar();
            LoadSelectedNote();
        };

        return card;
    }

    // ── Editor ──────────────────────────────────────────────────

    private void LoadSelectedNote()
    {
        var note = ViewModel.SelectedNote;

        // Always show the editor — if no note selected, show empty draft
        EditorEmptyState.Visibility = Visibility.Collapsed;
        TitleBox.Visibility = Visibility.Visible;
        Editor.Visibility = Visibility.Visible;
        SaveStatusText.Visibility = Visibility.Visible;

        if (note is null)
        {
            // Draft mode: empty editor, not saved yet
            _isDraft = true;
            _isLoadingNote = true;
            TitleBox.Text = "";
            Editor.Document.SetText(TextSetOptions.None, "");
            MetadataText.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Collapsed;
            SaveStatusText.Text = "";
            _isLoadingNote = false;
            TitleBox.Focus(FocusState.Programmatic);
            return;
        }

        _isDraft = false;
        DeleteButton.Visibility = Visibility.Visible;
        MetadataText.Visibility = Visibility.Visible;

        _isLoadingNote = true;
        TitleBox.Text = note.Title;

        if (!string.IsNullOrEmpty(note.ContentRtf))
        {
            Editor.Document.SetText(TextSetOptions.FormatRtf, note.ContentRtf);
        }
        else if (!string.IsNullOrEmpty(note.ContentPlainText))
        {
            Editor.Document.SetText(TextSetOptions.None, note.ContentPlainText);
        }
        else
        {
            Editor.Document.SetText(TextSetOptions.None, "");
        }

        var created = note.CreatedAt.ToLocalTime();
        var modified = note.ModifiedAt.ToLocalTime();
        MetadataText.Text = $"Created: {FormatDate(created)}  \u00b7  Modified: {FormatDate(modified)}";

        SaveStatusText.Text = "";
        _isLoadingNote = false;
    }

    private static string FormatDate(DateTime dt)
    {
        var today = DateTime.Now.Date;
        if (dt.Date == today) return $"Today, {dt:h:mm tt}";
        if (dt.Date == today.AddDays(-1)) return $"Yesterday, {dt:h:mm tt}";
        return dt.ToString("MMM d, h:mm tt");
    }

    // ── Auto-save ───────────────────────────────────────────────

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoadingNote) ScheduleAutoSave();
    }

    private void Editor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingNote) ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        SaveStatusText.Text = "";
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoSaveTimer.Tick += async (_, _) =>
            {
                _autoSaveTimer!.Stop();
                await SaveCurrentNote();
            };
        }
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async Task SaveCurrentNote()
    {
        try
        {
            Editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
            Editor.Document.GetText(TextGetOptions.None, out var plain);
            var plainTrimmed = plain.TrimEnd('\r', '\n');

            // Draft mode: create the note on first meaningful edit
            if (_isDraft)
            {
                var title = TitleBox.Text.Trim();
                if (string.IsNullOrEmpty(title) && string.IsNullOrWhiteSpace(plainTrimmed))
                    return; // Don't save empty drafts

                var item = await ViewModel.CreateAsync();
                _isDraft = false;

                // Now save with actual content
                await ViewModel.SaveNoteAsync(item, string.IsNullOrEmpty(title) ? "Untitled" : title, rtf, plainTrimmed);
                RebuildSidebar();

                // Show metadata and delete button now that it's saved
                DeleteButton.Visibility = Visibility.Visible;
                MetadataText.Visibility = Visibility.Visible;
                var c = item.CreatedAt.ToLocalTime();
                var m = item.ModifiedAt.ToLocalTime();
                MetadataText.Text = $"Created: {FormatDate(c)}  \u00b7  Modified: {FormatDate(m)}";
                SaveStatusText.Text = ViewModel.SaveStatus;
                return;
            }

            var note = ViewModel.SelectedNote;
            if (note is null) return;

            await ViewModel.SaveNoteAsync(note, TitleBox.Text, rtf, plainTrimmed);
            SaveStatusText.Text = ViewModel.SaveStatus;

            var modified = note.ModifiedAt.ToLocalTime();
            var created = note.CreatedAt.ToLocalTime();
            MetadataText.Text = $"Created: {FormatDate(created)}  \u00b7  Modified: {FormatDate(modified)}";
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to save note");
            SaveStatusText.Text = "Save failed";
        }
    }

    // ── Delete ──────────────────────────────────────────────────

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var note = ViewModel.SelectedNote;
        if (note is null) return;

        try
        {
            if (await DialogHelper.ConfirmDeleteAsync(Content.XamlRoot, "this note"))
            {
                await ViewModel.DeleteAsync(note);
                RebuildSidebar();
                LoadSelectedNote();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to delete note");
        }
    }

    // ── Formatting Toolbar ──────────────────────────────────────

    private Button? _boldBtn, _italicBtn, _underlineBtn, _bulletBtn, _numberBtn, _checkBtn;

    private void BuildToolbar()
    {
        _boldBtn = MakeToolbarButton("\uE8DD", "Bold");
        _boldBtn.Click += (_, _) => ToggleCharFormat(f => f.Bold = f.Bold == FormatEffect.On ? FormatEffect.Off : FormatEffect.On);
        ToolbarPanel.Children.Add(_boldBtn);

        _italicBtn = MakeToolbarButton("\uE8DB", "Italic");
        _italicBtn.Click += (_, _) => ToggleCharFormat(f => f.Italic = f.Italic == FormatEffect.On ? FormatEffect.Off : FormatEffect.On);
        ToolbarPanel.Children.Add(_italicBtn);

        _underlineBtn = MakeToolbarButton("\uE8DC", "Underline");
        _underlineBtn.Click += (_, _) => ToggleCharFormat(f => f.Underline = f.Underline == UnderlineType.Single ? UnderlineType.None : UnderlineType.Single);
        ToolbarPanel.Children.Add(_underlineBtn);

        // Separator
        ToolbarPanel.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            Margin = new Thickness(4, 0, 4, 0),
        });

        _bulletBtn = MakeToolbarButton("\uE8FD", "Bullet list");
        _bulletBtn.Click += (_, _) => ToggleListFormat(MarkerType.Bullet);
        ToolbarPanel.Children.Add(_bulletBtn);

        _numberBtn = MakeToolbarButton("\uE9D5", "Numbered list");
        _numberBtn.Click += (_, _) => ToggleListFormat(MarkerType.Arabic);
        ToolbarPanel.Children.Add(_numberBtn);

        _checkBtn = MakeToolbarButton("\uE73A", "Checklist");
        _checkBtn.Click += (_, _) => ToggleChecklist();
        ToolbarPanel.Children.Add(_checkBtn);
    }

    private static Button MakeToolbarButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void ToggleCharFormat(Action<ITextCharacterFormat> toggle)
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;
        var format = sel.CharacterFormat;
        toggle(format);
        sel.CharacterFormat = format;
        UpdateToolbarState();
    }

    private void ToggleListFormat(MarkerType marker)
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;
        var format = sel.ParagraphFormat;
        format.ListType = format.ListType == marker ? MarkerType.None : marker;
        sel.ParagraphFormat = format;
        UpdateToolbarState();
    }

    private void ToggleChecklist()
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;

        sel.GetText(TextGetOptions.None, out var text);

        if (text.StartsWith("\u2611"))
        {
            sel.SetText(TextSetOptions.None, "\u2610" + text[1..]);
        }
        else if (text.StartsWith("\u2610"))
        {
            sel.SetText(TextSetOptions.None, text[1..].TrimStart());
        }
        else
        {
            sel.SetText(TextSetOptions.None, "\u2610 " + text);
        }
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateToolbarState();
    }

    private void UpdateToolbarState()
    {
        var sel = Editor.Document.Selection;
        if (sel is null) return;

        var cf = sel.CharacterFormat;
        var pf = sel.ParagraphFormat;

        SetToolbarActive(_boldBtn, cf.Bold == FormatEffect.On);
        SetToolbarActive(_italicBtn, cf.Italic == FormatEffect.On);
        SetToolbarActive(_underlineBtn, cf.Underline == UnderlineType.Single);
        SetToolbarActive(_bulletBtn, pf.ListType == MarkerType.Bullet);
        SetToolbarActive(_numberBtn, pf.ListType == MarkerType.Arabic);
    }

    private void SetToolbarActive(Button? btn, bool active)
    {
        if (btn is null) return;
        btn.Background = active
            ? (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (btn.Content is FontIcon icon)
        {
            icon.Foreground = active
                ? (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"]
                : (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
        }
    }
}
