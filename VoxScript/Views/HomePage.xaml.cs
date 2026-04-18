using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.Core.Home;
using VoxScript.Core.Transcription.Core;
using VoxScript.Infrastructure;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class HomePage : Page
{
    private HomeViewModel _vm = null!;
    private VoxScriptEngine _engine = null!;

    private string _fullLatestText = "";

    public HomePage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_vm is null)
        {
            _vm = ServiceLocator.Get<HomeViewModel>();
            _engine = ServiceLocator.Get<VoxScriptEngine>();

            _engine.TranscriptionCompleted += OnTranscriptionCompleted;
        }

        _ = RefreshAllAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_engine is not null)
            _engine.TranscriptionCompleted -= OnTranscriptionCompleted;
        _vm = null!;
    }

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_vm is null) return;
            await _vm.OnTranscriptionCompletedAsync(text, CancellationToken.None);
            ApplyStats();
            ApplyLatestTranscript();
        });
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_fullLatestText))
        {
            var package = new DataPackage();
            package.SetText(_fullLatestText);
            Clipboard.SetContent(package);
        }
    }

    private void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is VoxScript.Shell.MainWindow mainWindow)
            mainWindow.NavigateTo(typeof(HistoryPage));
    }

    private async Task RefreshAllAsync()
    {
        await _vm.RefreshAsync(CancellationToken.None);
        ApplyStatuses();
        ApplyStats();
        ApplyLatestTranscript();
    }

    private void ApplyStatuses()
    {
        ApplyDot(OverallDot, _vm.OverallStatus.Level);
        OverallLabel.Text = _vm.OverallStatus.Label;

        ApplyDot(ModelDot, _vm.ModelStatus.Level);
        ModelLabel.Text = _vm.ModelStatus.Label;

        ApplyDot(AiDot, _vm.AiEnhanceStatus.Level);
        AiLabel.Text = _vm.AiEnhanceStatus.Label;

        ApplyDot(LlmDot, _vm.LlmFormatStatus.Level);
        LlmLabel.Text = _vm.LlmFormatStatus.Label;
    }

    private void ApplyStats()
    {
        TotalWordsText.Text = _vm.TotalWords.ToString("N0");
        AvgWpmText.Text = _vm.AvgWpm > 0
            ? ((int)Math.Round(_vm.AvgWpm)).ToString()
            : "—";

        RenderGraph(_vm.HourlyBuckets);
    }

    private void ApplyLatestTranscript()
    {
        if (_vm.HasLatestTranscript)
        {
            _fullLatestText = _vm.LatestTranscriptText;
            TranscriptText.Text = _vm.LatestTranscriptText;
            TranscriptText.Visibility = Visibility.Visible;
            EmptyText.Visibility = Visibility.Collapsed;

            var ts = _vm.LatestTranscriptTimestamp;
            TimestampText.Text = ts.HasValue
                ? FormatRelative(ts.Value)
                : "";
        }
        else
        {
            _fullLatestText = "";
            TranscriptText.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            TimestampText.Text = "";
        }
    }

    private void RenderGraph(IReadOnlyList<int> buckets)
    {
        GraphBars.ColumnDefinitions.Clear();
        GraphBars.Children.Clear();

        for (int i = 0; i < buckets.Count; i++)
            GraphBars.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int max = buckets.Count > 0 ? buckets.Max() : 0;
        if (max == 0) return; // Only baseline visible when no activity

        const double maxBarHeight = 80.0;
        const double minBarHeight = 6.0;

        for (int i = 0; i < buckets.Count; i++)
        {
            int count = buckets[i];
            if (count <= 0) continue;

            double barHeight = Math.Max(minBarHeight, (count / (double)max) * maxBarHeight);
            var bar = new Border
            {
                Height = barHeight,
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"],
                Margin = new Thickness(2, 0, 2, 0),
            };
            Grid.SetColumn(bar, i);
            GraphBars.Children.Add(bar);
        }
    }

    private void ApplyDot(Ellipse dot, StatusLevel level)
    {
        dot.Fill = level switch
        {
            StatusLevel.Ready => (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"],
            StatusLevel.Warming => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 140, 40)),
            StatusLevel.Unavailable => (SolidColorBrush)Application.Current.Resources["BrandRecordingBrush"],
            StatusLevel.Off => (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
            _ => (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"],
        };
    }

    private static string FormatRelative(DateTimeOffset ts)
    {
        var diff = DateTimeOffset.Now - ts;
        if (diff.TotalSeconds < 60) return "· just now";
        if (diff.TotalMinutes < 60) return $"· {(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"· {(int)diff.TotalHours}h ago";
        return "· " + ts.LocalDateTime.ToString("MMM d");
    }
}
