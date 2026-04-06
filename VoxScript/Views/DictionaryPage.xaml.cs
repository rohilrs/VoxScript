using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoxScript.Helpers;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class DictionaryPage : Page
{
    public DictionaryViewModel ViewModel { get; } = new();

    public DictionaryPage()
    {
        this.InitializeComponent();
        ViewModel.Words.CollectionChanged += (_, _) => RebuildWordList();
        ViewModel.Corrections.CollectionChanged += (_, _) => RebuildCorrectionList();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        RebuildWordList();
        RebuildCorrectionList();
    }

    // ── Sort handlers ───────────────────────────────────────────

    private void WordSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.WordSort = WordSortCombo.SelectedIndex switch
        {
            0 => SortMode.Newest, 1 => SortMode.Oldest, 2 => SortMode.Alphabetical,
            _ => SortMode.Alphabetical,
        };
    }

    private void CorrectionSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.CorrectionSort = CorrectionSortCombo.SelectedIndex switch
        {
            0 => SortMode.Newest, 1 => SortMode.Oldest, 2 => SortMode.Alphabetical,
            _ => SortMode.Newest,
        };
    }

    // ── Word actions ────────────────────────────────────────────

    private async void AddWordButton_Click(object sender, RoutedEventArgs e)
    {
        var word = NewWordBox.Text.Trim();
        if (!string.IsNullOrEmpty(word))
        {
            await ViewModel.AddWordAsync(word);
            NewWordBox.Text = "";
        }
    }

    private async Task ShowEditWordDialog(string oldWord)
    {
        var textBox = new TextBox { Text = oldWord, FontSize = 14, Header = "Word" };
        var dialog = new ContentDialog
        {
            Title = "Edit Word",
            Content = textBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var newWord = textBox.Text.Trim();
            if (!string.IsNullOrEmpty(newWord))
                await ViewModel.EditWordAsync(oldWord, newWord);
        }
    }

    // ── Correction actions ──────────────────────────────────────

    private async void AddCorrectionButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await ShowCorrectionDialog("Add Correction", "", "");
        if (result is not null)
            await ViewModel.AddCorrectionAsync(result.Value.wrong, result.Value.correct);
    }

    private async Task ShowEditCorrectionDialog(CorrectionItem item)
    {
        var result = await ShowCorrectionDialog("Edit Correction", item.Wrong, item.Correct);
        if (result is not null)
            await ViewModel.EditCorrectionAsync(item, result.Value.wrong, result.Value.correct);
    }

    private async Task<(string wrong, string correct)?> ShowCorrectionDialog(
        string title, string wrong, string correct)
    {
        var wrongBox = new TextBox { PlaceholderText = "e.g. teh", Text = wrong, FontSize = 14, Header = "When VoxScript writes" };
        var correctBox = new TextBox { PlaceholderText = "e.g. the", Text = correct, FontSize = 14, Header = "Replace with" };

        var panel = new StackPanel { Spacing = 16, MinWidth = 350 };
        panel.Children.Add(wrongBox);
        panel.Children.Add(correctBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var w = wrongBox.Text.Trim();
            var c = correctBox.Text.Trim();
            if (!string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(c))
                return (w, c);
        }
        return null;
    }

    // ── Word list rendering ─────────────────────────────────────

    private void RebuildWordList()
    {
        WordList.Children.Clear();

        foreach (var word in ViewModel.Words)
        {
            var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(4, 8, 4, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var wordText = new TextBlock
            {
                Text = word, FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"],
            };
            Grid.SetColumn(wordText, 0);
            row.Children.Add(wordText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            var capturedWord = word;
            var editBtn = MakeIconButton("\uE70F", "Edit", (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"]);
            editBtn.Click += async (_, _) => await ShowEditWordDialog(capturedWord);
            buttons.Children.Add(editBtn);

            var deleteBtn = MakeIconButton("\uE74D", "Remove", new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)));
            deleteBtn.Click += async (_, _) =>
            {
                if (await DialogHelper.ConfirmDeleteAsync(this.XamlRoot, $"word \"{capturedWord}\""))
                    await ViewModel.DeleteWordAsync(capturedWord);
            };
            buttons.Children.Add(deleteBtn);

            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);

            WordList.Children.Add(new Border
            {
                Child = row,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            });
        }

        WordEmptyState.Visibility = ViewModel.HasWords ? Visibility.Collapsed : Visibility.Visible;
        WordCountText.Text = ViewModel.HasWords
            ? $"{ViewModel.WordCount} word{(ViewModel.WordCount == 1 ? "" : "s")}"
            : "";
    }

    // ── Correction list rendering ───────────────────────────────

    private void RebuildCorrectionList()
    {
        CorrectionList.Children.Clear();

        foreach (var item in ViewModel.Corrections)
        {
            var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(4, 10, 4, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var wrongBadge = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BrandBackgroundBrush"],
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = item.Wrong, FontFamily = new FontFamily("Consolas"), FontSize = 14 },
            };
            Grid.SetColumn(wrongBadge, 0);
            row.Children.Add(wrongBadge);

            var arrow = new TextBlock
            {
                Text = "→", FontSize = 16,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(arrow, 1);
            row.Children.Add(arrow);

            var correctText = new TextBlock
            {
                Text = item.Correct, FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(correctText, 2);
            row.Children.Add(correctText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var capturedItem = item;

            var editBtn = MakeIconButton("\uE70F", "Edit", (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"]);
            editBtn.Click += async (_, _) => await ShowEditCorrectionDialog(capturedItem);
            buttons.Children.Add(editBtn);

            var deleteBtn = MakeIconButton("\uE74D", "Delete", new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)));
            deleteBtn.Click += async (_, _) =>
            {
                if (await DialogHelper.ConfirmDeleteAsync(this.XamlRoot, $"correction \"{capturedItem.Wrong}\""))
                    await ViewModel.DeleteCorrectionAsync(capturedItem);
            };
            buttons.Children.Add(deleteBtn);

            Grid.SetColumn(buttons, 3);
            row.Children.Add(buttons);

            CorrectionList.Children.Add(new Border
            {
                Child = row,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            });
        }

        CorrectionEmptyState.Visibility = ViewModel.HasCorrections ? Visibility.Collapsed : Visibility.Visible;
        CorrectionCountText.Text = ViewModel.HasCorrections
            ? $"{ViewModel.CorrectionCount} correction{(ViewModel.CorrectionCount == 1 ? "" : "s")}"
            : "";
    }

    private static Button MakeIconButton(string glyph, string tooltip, SolidColorBrush foreground)
    {
        var btn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = foreground },
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }
}
