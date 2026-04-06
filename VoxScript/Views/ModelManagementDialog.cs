// VoxScript/Views/ModelManagementDialog.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoxScript.ViewModels;
using Windows.Storage.Pickers;

namespace VoxScript.Views;

public static class ModelManagementDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        var vm = new ModelManagementViewModel();
        var modelList = new StackPanel { Spacing = 2 };
        var progressBar = new ProgressBar
        {
            Minimum = 0, Maximum = 1,
            IsIndeterminate = false,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = GetBrush("BrandPrimaryBrush"),
        };
        var progressText = new TextBlock
        {
            FontSize = 12,
            Foreground = GetBrush("BrandMutedBrush"),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };

        void RebuildList()
        {
            modelList.Children.Clear();
            foreach (var item in vm.Models)
                modelList.Children.Add(BuildModelRow(item, vm, xamlRoot, RebuildList,
                    progressBar, progressText));
        }

        RebuildList();

        // Custom model section
        var customSection = BuildCustomSection(vm, xamlRoot, RebuildList, progressBar, progressText);

        // Scrollable model list only
        var scrollableList = new ScrollViewer
        {
            Content = modelList,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var content = new StackPanel { Spacing = 16, MinWidth = 500 };
        content.Children.Add(scrollableList);
        content.Children.Add(progressBar);
        content.Children.Add(progressText);
        content.Children.Add(BuildSeparator());
        content.Children.Add(customSection);

        var dialog = new ContentDialog
        {
            Title = "Manage Models",
            Content = content,
            CloseButtonText = "Done",
            XamlRoot = xamlRoot,
            CornerRadius = new CornerRadius(12),
        };

        dialog.Resources["ContentDialogCommandSpaceBackground"] =
            new SolidColorBrush(Microsoft.UI.Colors.White);

        // Center the Done button by walking the visual tree after it opens
        dialog.Opened += (_, _) =>
        {
            // The command space is a Grid at the bottom of the ContentDialog template.
            // Find the close button and center it by making its column span the full width.
            var closeButton = FindDescendant<Button>(dialog, b => b.Content?.ToString() == "Done");
            if (closeButton?.Parent is Grid commandGrid)
            {
                // Collapse other columns, let the close button span all and center
                Grid.SetColumn(closeButton, 0);
                Grid.SetColumnSpan(closeButton, commandGrid.ColumnDefinitions.Count > 0
                    ? commandGrid.ColumnDefinitions.Count : 1);
                closeButton.HorizontalAlignment = HorizontalAlignment.Center;
                closeButton.MinWidth = 120;
            }
        };

        await dialog.ShowAsync();
    }

    private static Border BuildModelRow(ModelDisplayItem item, ModelManagementViewModel vm,
        XamlRoot xamlRoot, Action rebuild, ProgressBar progressBar, TextBlock progressText)
    {
        var border = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Background = item.IsActive
                ? GetBrush("BrandPrimaryLightBrush")
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: name + size + badges
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = GetBrush("BrandForegroundBrush"),
        });
        if (item.IsActive)
        {
            nameRow.Children.Add(new Border
            {
                Background = GetBrush("BrandSuccessBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Active",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                },
            });
        }
        info.Children.Add(nameRow);

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrEmpty(item.SizeText))
        {
            metaRow.Children.Add(new TextBlock
            {
                Text = item.SizeText,
                FontSize = 12,
                Foreground = GetBrush("BrandMutedBrush"),
            });
        }
        if (!item.IsDownloaded)
        {
            metaRow.Children.Add(new TextBlock
            {
                Text = "Not downloaded",
                FontSize = 12,
                Foreground = GetBrush("BrandMutedBrush"),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            });
        }
        if (!item.IsPredefined)
        {
            metaRow.Children.Add(new TextBlock
            {
                Text = "Custom",
                FontSize = 12,
                Foreground = GetBrush("BrandPrimaryBrush"),
            });
        }
        info.Children.Add(metaRow);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Right: action buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        if (!item.IsActive)
        {
            var useBtn = new Button
            {
                Content = item.IsDownloaded ? "Use" : "Download & Use",
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(6),
                Background = GetBrush("BrandPrimaryBrush"),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            };
            useBtn.Click += async (_, _) =>
            {
                useBtn.IsEnabled = false;
                useBtn.Content = "Loading...";

                if (!item.IsDownloaded)
                {
                    progressBar.Visibility = Visibility.Visible;
                    progressText.Visibility = Visibility.Visible;
                    progressText.Text = $"Downloading {item.DisplayName}...";
                    vm.PropertyChanged += OnProgress;
                }

                await vm.UseModelAsync(item.Name);

                progressBar.Visibility = Visibility.Collapsed;
                progressText.Visibility = Visibility.Collapsed;
                vm.PropertyChanged -= OnProgress;
                rebuild();
            };
            buttons.Children.Add(useBtn);

            void OnProgress(object? s, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(vm.DownloadProgress))
                {
                    progressBar.Value = vm.DownloadProgress;
                    progressText.Text = $"Downloading {item.DisplayName}... {vm.DownloadProgress:P0}";
                }
            }
        }

        if (item.IsDownloaded && !item.IsActive)
        {
            var deleteBtn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 204, 68, 68)),
                },
            };
            ToolTipService.SetToolTip(deleteBtn, "Delete model");
            deleteBtn.Click += async (_, _) =>
            {
                if (await Helpers.DialogHelper.ConfirmDeleteAsync(xamlRoot, $"model \"{item.DisplayName}\""))
                {
                    vm.DeleteModel(item.Name);
                    rebuild();
                }
            };
            buttons.Children.Add(deleteBtn);
        }

        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        border.Child = grid;
        return border;
    }

    private static StackPanel BuildCustomSection(ModelManagementViewModel vm, XamlRoot xamlRoot,
        Action rebuild, ProgressBar progressBar, TextBlock progressText)
    {
        var section = new StackPanel { Spacing = 12 };

        section.Children.Add(new TextBlock
        {
            Text = "ADD CUSTOM MODEL",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            CharacterSpacing = 120,
            Foreground = GetBrush("BrandMutedBrush"),
        });

        // Import local file
        var importBtn = new Button
        {
            Content = "Import local .bin file",
            FontSize = 13,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Background = GetBrush("BrandBackgroundBrush"),
            Foreground = GetBrush("BrandForegroundBrush"),
            BorderBrush = GetBrush("BrandPrimaryLightBrush"),
            BorderThickness = new Thickness(1),
        };
        importBtn.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".bin");

            // Initialize picker with window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await vm.ImportLocalFileAsync(file.Path);
                rebuild();
            }
        };
        section.Children.Add(importBtn);

        // Download from URL
        section.Children.Add(new TextBlock
        {
            Text = "Or download from URL:",
            FontSize = 13,
            Foreground = GetBrush("BrandMutedBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        });

        var nameBox = new TextBox
        {
            PlaceholderText = "Model name (e.g. ggml-medium.en)",
            FontSize = 13,
            Height = 36,
        };
        nameBox.TextChanged += (_, _) => vm.CustomName = nameBox.Text;
        section.Children.Add(nameBox);

        var urlBox = new TextBox
        {
            PlaceholderText = "https://huggingface.co/.../model.bin",
            FontSize = 13,
            Height = 36,
        };
        urlBox.TextChanged += (_, _) => vm.CustomUrl = urlBox.Text;
        section.Children.Add(urlBox);

        var downloadBtn = new Button
        {
            Content = "Download",
            FontSize = 13,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            Background = GetBrush("BrandPrimaryBrush"),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        downloadBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(urlBox.Text))
                return;

            downloadBtn.IsEnabled = false;
            downloadBtn.Content = "Downloading...";
            progressBar.Visibility = Visibility.Visible;
            progressText.Visibility = Visibility.Visible;
            progressText.Text = $"Downloading {nameBox.Text}...";

            vm.PropertyChanged += OnProgress;
            await vm.DownloadCustomAsync();
            vm.PropertyChanged -= OnProgress;

            progressBar.Visibility = Visibility.Collapsed;
            progressText.Visibility = Visibility.Collapsed;
            downloadBtn.IsEnabled = true;
            downloadBtn.Content = "Download";
            nameBox.Text = "";
            urlBox.Text = "";
            rebuild();

            void OnProgress(object? s, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(vm.DownloadProgress))
                {
                    progressBar.Value = vm.DownloadProgress;
                    progressText.Text = $"Downloading... {vm.DownloadProgress:P0}";
                }
            }
        };
        section.Children.Add(downloadBtn);

        return section;
    }

    private static Border BuildSeparator() => new()
    {
        Height = 1,
        Background = GetBrush("BrandPrimaryLightBrush"),
        Margin = new Thickness(0, 4, 0, 4),
    };

    private static SolidColorBrush GetBrush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && predicate(match))
                return match;
            var result = FindDescendant(child, predicate);
            if (result is not null)
                return result;
        }
        return null;
    }
}
