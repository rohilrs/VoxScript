using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoxScript.Helpers;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class ExpansionsPage : Page
{
    public ExpansionsViewModel ViewModel { get; } = new();

    public ExpansionsPage()
    {
        this.InitializeComponent();
        ViewModel.Expansions.CollectionChanged += (_, _) => RebuildList();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        RebuildList();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.CurrentSort = SortCombo.SelectedIndex switch
        {
            0 => SortMode.Newest,
            1 => SortMode.Oldest,
            2 => SortMode.Alphabetical,
            _ => SortMode.Newest,
        };
    }

    // ── Add/Edit popup dialog ───────────────────────────────────

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await ShowExpansionDialog("Add Expansion", "", "", false);
        if (result is not null)
            await ViewModel.AddAsync(result.Value.original, result.Value.replacement, result.Value.caseSensitive);
    }

    private async Task ShowEditDialog(ExpansionItem item)
    {
        var result = await ShowExpansionDialog("Edit Expansion", item.Original, item.Replacement, item.CaseSensitive);
        if (result is not null)
            await ViewModel.SaveEditAsync(item, result.Value.original, result.Value.replacement, result.Value.caseSensitive);
    }

    private async Task<(string original, string replacement, bool caseSensitive)?> ShowExpansionDialog(
        string title, string original, string replacement, bool caseSensitive)
    {
        var originalBox = new TextBox
        {
            PlaceholderText = "e.g. btw",
            Text = original,
            FontSize = 14,
            Header = "When I say",
        };
        var replacementBox = new TextBox
        {
            PlaceholderText = "e.g. by the way",
            Text = replacement,
            FontSize = 14,
            Header = "Replace with",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
        };
        var caseCheck = new CheckBox
        {
            Content = "Case sensitive (Aa)",
            IsChecked = caseSensitive,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 400 };
        panel.Children.Add(originalBox);
        panel.Children.Add(replacementBox);
        panel.Children.Add(caseCheck);

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
            var o = originalBox.Text.Trim();
            var r = replacementBox.Text.Trim();
            if (!string.IsNullOrEmpty(o) && !string.IsNullOrEmpty(r))
                return (o, r, caseCheck.IsChecked == true);
        }
        return null;
    }

    // ── List rendering ──────────────────────────────────────────

    private void RebuildList()
    {
        ExpansionsList.Children.Clear();

        foreach (var item in ViewModel.Expansions)
            ExpansionsList.Children.Add(BuildRow(item));

        EmptyState.Visibility = ViewModel.HasExpansions ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = ViewModel.HasExpansions
            ? $"{ViewModel.Count} expansion{(ViewModel.Count == 1 ? "" : "s")}"
            : "";
    }

    private Border BuildRow(ExpansionItem item)
    {
        var border = new Border
        {
            Padding = new Thickness(4, 10, 4, 10),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
        };

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Shorthand badge
        var badgePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["BrandBackgroundBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = item.Original, FontFamily = new FontFamily("Consolas"), FontSize = 14 },
        };
        badgePanel.Children.Add(badge);
        if (item.CaseSensitive)
        {
            badgePanel.Children.Add(new TextBlock
            {
                Text = "Aa", FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(badgePanel, 0);
        grid.Children.Add(badgePanel);

        // Replacement text
        var replacementText = new TextBlock
        {
            Text = item.Replacement, FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(replacementText, 1);
        grid.Children.Add(replacementText);

        // Buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        var editBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 14, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] },
        };
        ToolTipService.SetToolTip(editBtn, "Edit");
        editBtn.Click += async (_, _) => await ShowEditDialog(item);
        buttons.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)) },
        };
        ToolTipService.SetToolTip(deleteBtn, "Delete");
        deleteBtn.Click += async (_, _) =>
        {
            if (await DialogHelper.ConfirmDeleteAsync(this.XamlRoot, $"expansion \"{item.Original}\""))
                await ViewModel.DeleteAsync(item);
        };
        buttons.Children.Add(deleteBtn);

        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        border.Child = grid;
        return border;
    }
}
