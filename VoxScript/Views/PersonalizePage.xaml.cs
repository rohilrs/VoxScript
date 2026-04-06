using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoxScript.Core.AI;
using VoxScript.Core.PowerMode;
using VoxScript.Helpers;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class PersonalizePage : Page
{
    public PersonalizeViewModel ViewModel { get; } = new();

    private static readonly (string Name, string Description, EnhancementPreset Preset, string[] Tags)[] PresetDefs =
    [
        ("Formal", "Professional writing with full punctuation", EnhancementPreset.Formal,
            ["Standard punctuation", "Sentence case", "Remove fillers"]),
        ("Semi-casual", "Natural tone with light corrections", EnhancementPreset.SemiCasual,
            ["Minimal punctuation", "Sentence case", "Remove fillers"]),
        ("Casual", "Minimal cleanup, keeps your voice", EnhancementPreset.Casual,
            ["Minimal punctuation", "As spoken caps", "Remove fillers"]),
        ("Custom", "Full control over style and prompt", EnhancementPreset.Custom, []),
    ];

    public PersonalizePage()
    {
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        RebuildTabs();
    }

    // ── Tab bar ──────────────────────────────────────────────

    private void RebuildTabs()
    {
        TabBar.Children.Clear();

        for (int i = 0; i < ViewModel.PowerModes.Count; i++)
        {
            var mode = ViewModel.PowerModes[i];
            var index = i;
            var tab = new Button
            {
                Content = mode.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Padding = new Thickness(20, 10, 20, 10),
                CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = GetBrush("BrandMutedBrush"),
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            tab.Click += (_, _) => { ViewModel.SelectedTabIndex = index; UpdateTabHighlights(); BuildTabContent(); };
            TabBar.Children.Add(tab);
        }

        // Add "+" button
        var addTab = new Button
        {
            Content = "+",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = GetBrush("BrandPrimaryBrush"),
            BorderThickness = new Thickness(0),
        };
        addTab.Click += AddTab_Click;
        TabBar.Children.Add(addTab);

        // Select first tab
        if (ViewModel.PowerModes.Count > 0)
        {
            ViewModel.SelectedTabIndex = 0;
            UpdateTabHighlights();
            BuildTabContent();
        }
    }

    private void UpdateTabHighlights()
    {
        for (int i = 0; i < ViewModel.PowerModes.Count && i < TabBar.Children.Count; i++)
        {
            var btn = (Button)TabBar.Children[i];
            var isSelected = i == ViewModel.SelectedTabIndex;
            btn.BorderBrush = isSelected ? GetBrush("BrandPrimaryBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btn.Foreground = isSelected ? GetBrush("BrandPrimaryBrush") : GetBrush("BrandMutedBrush");
        }
    }

    // ── Tab content ──────────────────────────────────────────

    private void BuildTabContent()
    {
        TabContent.Children.Clear();
        if (ViewModel.SelectedTabIndex < 0 || ViewModel.SelectedTabIndex >= ViewModel.PowerModes.Count) return;

        var mode = ViewModel.PowerModes[ViewModel.SelectedTabIndex];

        // Header: name + toggle
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameStack = new StackPanel();
        nameStack.Children.Add(new TextBlock
        {
            Text = mode.Name,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = GetBrush("BrandForegroundBrush"),
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = "Auto-activates when using matched apps",
            FontSize = 13,
            Foreground = GetBrush("BrandMutedBrush"),
        });
        Grid.SetColumn(nameStack, 0);
        header.Children.Add(nameStack);

        var toggle = new ToggleSwitch { IsOn = mode.IsEnabled, MinWidth = 0, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
        var modeId = mode.Id;
        toggle.Toggled += async (_, _) => await ViewModel.ToggleModeAsync(modeId, toggle.IsOn);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);

        TabContent.Children.Add(header);

        // Style presets card
        TabContent.Children.Add(BuildStyleCard(mode));

        // Apps card
        TabContent.Children.Add(BuildAppsCard(mode));

        // URL pattern card
        TabContent.Children.Add(BuildUrlCard(mode));

        // Custom prompt card (only for Custom preset)
        if (mode.Preset == EnhancementPreset.Custom)
            TabContent.Children.Add(BuildPromptCard(mode));

        // Delete button (non-built-in only)
        if (!mode.IsBuiltIn)
        {
            var deleteBtn = new Button
            {
                Content = "Delete this mode",
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                FontSize = 13,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 0),
            };
            deleteBtn.Click += async (_, _) =>
            {
                if (await DialogHelper.ConfirmDeleteAsync(this.XamlRoot, $"mode \"{mode.Name}\""))
                {
                    await ViewModel.DeleteModeAsync(modeId);
                    RebuildTabs();
                }
            };
            TabContent.Children.Add(deleteBtn);
        }
    }

    private Border BuildStyleCard(PowerModeConfig mode)
    {
        var card = MakeCard("STYLE", "\uE771");
        var stack = (StackPanel)((Border)card).Child!;

        for (int i = 0; i < PresetDefs.Length; i++)
        {
            var (name, desc, preset, tags) = PresetDefs[i];
            var isSelected = mode.Preset == preset;

            var presetBorder = new Border
            {
                Background = GetBrush("BrandCardBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 14, 20, 14),
                BorderThickness = new Thickness(2),
                BorderBrush = isSelected ? GetBrush("BrandPrimaryBrush") : GetBrush("BrandPrimaryLightBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            };

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = GetBrush("BrandForegroundBrush"),
            });
            content.Children.Add(new TextBlock { Text = desc, FontSize = 12, Foreground = GetBrush("BrandMutedBrush") });

            if (tags.Length > 0)
            {
                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
                foreach (var tag in tags)
                {
                    tagPanel.Children.Add(new Border
                    {
                        Background = GetBrush("BrandBackgroundBrush"),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(8, 3, 8, 3),
                        BorderBrush = GetBrush("BrandPrimaryLightBrush"),
                        BorderThickness = new Thickness(1),
                        Child = new TextBlock { Text = tag, FontSize = 11, Foreground = GetBrush("BrandMutedBrush") },
                    });
                }
                content.Children.Add(tagPanel);
            }

            presetBorder.Child = content;

            var targetPreset = preset;
            var modeId = mode.Id;
            presetBorder.PointerPressed += async (_, _) =>
            {
                await ViewModel.SetModePresetAsync(modeId, targetPreset);
                await ViewModel.LoadAsync();
                BuildTabContent();
            };

            // Hover
            presetBorder.PointerEntered += (s, _) =>
            {
                if (mode.Preset != targetPreset) ((Border)s).BorderBrush = GetBrush("BrandPrimaryBrush");
            };
            presetBorder.PointerExited += (s, _) =>
            {
                if (mode.Preset != targetPreset) ((Border)s).BorderBrush = GetBrush("BrandPrimaryLightBrush");
            };

            stack.Children.Add(presetBorder);
        }

        return card;
    }

    private Border BuildAppsCard(PowerModeConfig mode)
    {
        var card = MakeCard("APPS", "\uE77B");
        var stack = (StackPanel)((Border)card).Child!;

        stack.Children.Add(new TextBlock
        {
            Text = "Process names that trigger this mode",
            FontSize = 13,
            Foreground = GetBrush("BrandMutedBrush"),
        });

        // Chips
        var chipPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        chipPanel.Children.Clear();

        var wrapGrid = new ItemsControl();
        var chipsWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        foreach (var app in mode.GetProcessNames())
        {
            var chip = new Border
            {
                Background = GetBrush("BrandBackgroundBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 6, 5),
                BorderBrush = GetBrush("BrandPrimaryLightBrush"),
                BorderThickness = new Thickness(1),
            };

            var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chipContent.Children.Add(new TextBlock { Text = app, FontSize = 13, Foreground = GetBrush("BrandForegroundBrush"), VerticalAlignment = VerticalAlignment.Center });

            var removeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10, Foreground = GetBrush("BrandMutedBrush") },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 0,
                MinHeight = 0,
                CornerRadius = new CornerRadius(4),
            };
            var appName = app;
            var modeId = mode.Id;
            removeBtn.Click += async (_, _) =>
            {
                await ViewModel.RemoveAppFromModeAsync(modeId, appName);
                await ViewModel.LoadAsync();
                BuildTabContent();
            };
            chipContent.Children.Add(removeBtn);

            chip.Child = chipContent;
            chipsWrap.Children.Add(chip);
        }
        stack.Children.Add(chipsWrap);

        // Add app field
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var addBox = new TextBox { PlaceholderText = "Add app name...", FontSize = 13, Width = 200 };
        var addBtn = new Button
        {
            Content = "Add",
            FontSize = 13,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(6),
            Background = GetBrush("BrandPrimaryBrush"),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        var addModeId = mode.Id;
        addBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(addBox.Text)) return;
            await ViewModel.AddAppToModeAsync(addModeId, addBox.Text);
            await ViewModel.LoadAsync();
            BuildTabContent();
        };
        addRow.Children.Add(addBox);
        addRow.Children.Add(addBtn);
        stack.Children.Add(addRow);

        return card;
    }

    private Border BuildUrlCard(PowerModeConfig mode)
    {
        var card = MakeCard("URLS", "\uE774");
        var stack = (StackPanel)((Border)card).Child!;

        stack.Children.Add(new TextBlock
        {
            Text = "Browser URLs that trigger this mode",
            FontSize = 13,
            Foreground = GetBrush("BrandMutedBrush"),
        });

        // Chips
        var chipsWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var url in mode.GetUrlPatterns())
        {
            var chip = new Border
            {
                Background = GetBrush("BrandBackgroundBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 6, 5),
                BorderBrush = GetBrush("BrandPrimaryLightBrush"),
                BorderThickness = new Thickness(1),
            };

            var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chipContent.Children.Add(new TextBlock { Text = url, FontSize = 13, Foreground = GetBrush("BrandForegroundBrush"), VerticalAlignment = VerticalAlignment.Center });

            var removeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10, Foreground = GetBrush("BrandMutedBrush") },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 0,
                MinHeight = 0,
                CornerRadius = new CornerRadius(4),
            };
            var urlName = url;
            var modeId = mode.Id;
            removeBtn.Click += async (_, _) =>
            {
                await ViewModel.RemoveUrlFromModeAsync(modeId, urlName);
                await ViewModel.LoadAsync();
                BuildTabContent();
            };
            chipContent.Children.Add(removeBtn);

            chip.Child = chipContent;
            chipsWrap.Children.Add(chip);
        }
        stack.Children.Add(chipsWrap);

        // Add URL field
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var addBox = new TextBox { PlaceholderText = "Add URL (e.g. mail.google.com)", FontSize = 13, Width = 260 };
        var addBtn = new Button
        {
            Content = "Add",
            FontSize = 13,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(6),
            Background = GetBrush("BrandPrimaryBrush"),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        var addModeId = mode.Id;
        addBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(addBox.Text)) return;
            await ViewModel.AddUrlToModeAsync(addModeId, addBox.Text);
            await ViewModel.LoadAsync();
            BuildTabContent();
        };
        addRow.Children.Add(addBox);
        addRow.Children.Add(addBtn);
        stack.Children.Add(addRow);

        return card;
    }

    private Border BuildPromptCard(PowerModeConfig mode)
    {
        var card = MakeCard("SYSTEM PROMPT", "\uE70B");
        var stack = (StackPanel)((Border)card).Child!;

        stack.Children.Add(new TextBlock
        {
            Text = "The instruction sent to the AI for this mode.",
            FontSize = 13,
            Foreground = GetBrush("BrandMutedBrush"),
        });

        var promptBox = new TextBox
        {
            Text = mode.SystemPrompt ?? "",
            PlaceholderText = "Enter a custom system prompt...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            MaxHeight = 250,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var modeId = mode.Id;
        promptBox.LostFocus += async (_, _) =>
        {
            await ViewModel.SetModePromptAsync(modeId, promptBox.Text);
        };
        stack.Children.Add(promptBox);

        return card;
    }

    // ── Add new mode ─────────────────────────────────────────

    private async void AddTab_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Mode name", FontSize = 14 };
        var dialog = new ContentDialog
        {
            Title = "Add Context Mode",
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            CornerRadius = new CornerRadius(12),
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await ViewModel.AddCustomModeAsync(nameBox.Text.Trim());
            ViewModel.SelectedTabIndex = ViewModel.PowerModes.Count - 1;
            RebuildTabs();
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private static Border MakeCard(string label, string glyph)
    {
        var border = new Border
        {
            Background = GetBrush("BrandCardBrush"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28),
            BorderBrush = GetBrush("BrandPrimaryLightBrush"),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel { Spacing = 12 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, Foreground = GetBrush("BrandPrimaryBrush") });
        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            CharacterSpacing = 120,
            Foreground = GetBrush("BrandMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        border.Child = stack;
        return border;
    }

    private static SolidColorBrush GetBrush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
