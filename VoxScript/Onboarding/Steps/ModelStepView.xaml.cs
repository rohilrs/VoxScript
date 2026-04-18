using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VoxScript.Onboarding.Steps;

public sealed partial class ModelStepView : UserControl
{
    private readonly ModelStepViewModel _vm;
    private readonly Border[] _cards = new Border[3];

    public ModelStepView(ModelStepViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BuildCards();
        ApplyState();
        _vm.PropertyChanged += OnVmChanged;
    }

    private void BuildCards()
    {
        for (int i = 0; i < ModelStepViewModel.Choices.Length; i++)
        {
            var choice = ModelStepViewModel.Choices[i];
            var card = new Border
            {
                Width = 190,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(12, 12, 12, 14),
                Background = new SolidColorBrush(Color.FromArgb(255, 255, 254, 251)),
            };
            var sp = new StackPanel { Spacing = 4 };
            sp.Children.Add(new TextBlock
            {
                Text = choice.Label,
                FontFamily = new FontFamily("Georgia"),
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{choice.Model.Name} · ~{FormatSize(choice.Model.FileSizeBytes ?? 0)}",
                FontSize = 10,
                Foreground = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            });
            sp.Children.Add(new TextBlock
            {
                Text = choice.Description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            card.Child = sp;

            int captured = i;
            card.Tapped += (_, _) => { _vm.SelectedChoiceIndex = captured; ApplyCardSelection(); };
            _cards[i] = card;
            CardsHost.Children.Add(card);
        }

        ApplyCardSelection();
    }

    private void ApplyCardSelection()
    {
        var accent = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"];
        var muted = new SolidColorBrush(Color.FromArgb(255, 217, 209, 196));
        for (int i = 0; i < _cards.Length; i++)
            _cards[i].BorderBrush = i == _vm.SelectedChoiceIndex ? accent : muted;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F1} GB";
        if (bytes >= 1_000_000)     return $"{bytes / 1_000_000.0:F0} MB";
        return $"{bytes / 1_000.0:F0} KB";
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

    private void ApplyState()
    {
        PickerPanel.Visibility      = _vm.SubState == ModelSubState.Picker      ? Visibility.Visible : Visibility.Collapsed;
        DownloadingPanel.Visibility = _vm.SubState == ModelSubState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        DonePanel.Visibility        = _vm.SubState == ModelSubState.Done        ? Visibility.Visible : Visibility.Collapsed;
        FailedPanel.Visibility      = _vm.SubState == ModelSubState.Failed      ? Visibility.Visible : Visibility.Collapsed;

        if (_vm.SubState == ModelSubState.Downloading)
        {
            DownloadTitle.Text = $"Downloading {ModelStepViewModel.Choices[_vm.SelectedChoiceIndex].Label} model";
            DownloadBar.Value = _vm.DownloadProgress;
            DownloadPct.Text = $"{_vm.DownloadProgress:P0}";
        }
        else if (_vm.SubState == ModelSubState.Done)
        {
            var choice = ModelStepViewModel.Choices[_vm.SelectedChoiceIndex];
            DoneSubtitle.Text = $"✓ {choice.Model.Name} is installed and loaded.";
        }
        else if (_vm.SubState == ModelSubState.Failed)
        {
            FailedMessage.Text = _vm.ErrorMessage ?? "Unknown error.";
        }

        ApplyCardSelection();
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = _vm.StartDownloadAsync();
    private void ReturnToPicker_Click(object sender, RoutedEventArgs e) => _vm.ReturnToPicker();
}
