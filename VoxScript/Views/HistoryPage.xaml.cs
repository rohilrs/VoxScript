using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.Helpers;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = new();
    private DispatcherTimer? _searchDebounce;

    public HistoryPage()
    {
        this.InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.IsEmpty) or nameof(ViewModel.HasMore))
                UpdateVisibility();
        };
        ViewModel.Groups.CollectionChanged += (_, _) => RebuildList();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        RebuildList();
        UpdateVisibility();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce?.Stop();
            await ViewModel.SearchAsync();
            RebuildList();
            UpdateVisibility();
        };
        _searchDebounce.Start();
    }

    private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadMoreAsync();
        RebuildList();
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        EmptyState.Visibility = ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.Visibility = ViewModel.HasMore && !ViewModel.IsSearching ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildList()
    {
        HistoryList.Children.Clear();

        foreach (var group in ViewModel.Groups)
        {
            // Date header
            HistoryList.Children.Add(new TextBlock
            {
                Text = group.DateLabel,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 80,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
                Margin = new Thickness(0, 0, 0, 8),
            });

            // Items in this group
            var groupPanel = new StackPanel { Spacing = 8 };
            foreach (var item in group.Items)
                groupPanel.Children.Add(BuildCard(item));

            HistoryList.Children.Add(groupPanel);
        }
    }

    private Border BuildCard(HistoryItem item)
    {
        var card = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["BrandCardBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
            BorderThickness = new Thickness(1),
        };

        var outer = new StackPanel { Spacing = 8 };

        // Header row: time + badges + actions
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: time + badges
        var metaPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        metaPanel.Children.Add(new TextBlock
        {
            Text = item.TimeDisplay,
            FontSize = 12,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
        });

        if (!string.IsNullOrEmpty(item.ModelName))
        {
            metaPanel.Children.Add(new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryLightBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = item.ModelName,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Foreground = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"],
                },
            });
        }

        if (item.WasAiEnhanced)
        {
            metaPanel.Children.Add(new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = "\u2728 Enhanced",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                },
            });
        }

        Grid.SetColumn(metaPanel, 0);
        header.Children.Add(metaPanel);

        // Right: copy + delete
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 14, Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"] };
        var copyBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            Content = copyIcon,
        };
        ToolTipService.SetToolTip(copyBtn, "Copy to clipboard");
        copyBtn.Click += async (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(item.DisplayText);
            Clipboard.SetContent(dp);

            // Show "Copied!" feedback
            copyIcon.Glyph = "\uE73E"; // Checkmark
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"];
            ToolTipService.SetToolTip(copyBtn, "Copied!");
            await Task.Delay(1500);
            copyIcon.Glyph = "\uE8C8"; // Back to copy icon
            copyIcon.Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
            ToolTipService.SetToolTip(copyBtn, "Copy to clipboard");
        };
        actions.Children.Add(copyBtn);

        var deleteBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)) },
        };
        ToolTipService.SetToolTip(deleteBtn, "Delete");
        deleteBtn.Click += async (_, _) =>
        {
            var preview = item.DisplayText.Length > 40 ? item.DisplayText[..40] + "..." : item.DisplayText;
            if (await DialogHelper.ConfirmDeleteAsync(this.XamlRoot, $"this transcription"))
            {
                await ViewModel.DeleteAsync(item);
                RebuildList();
                UpdateVisibility();
            }
        };
        actions.Children.Add(deleteBtn);

        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        outer.Children.Add(header);

        // Transcription text
        outer.Children.Add(new TextBlock
        {
            Text = item.DisplayText,
            FontSize = 15,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"],
        });

        card.Child = outer;
        return card;
    }
}
