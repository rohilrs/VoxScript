using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
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

        RenderGraph(_vm.ActivityBuckets);
    }

    private void GraphHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-render when the container is measured or resized so the polyline coordinates match.
        if (_vm is not null)
            RenderGraph(_vm.ActivityBuckets);
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
        // Clear any prior line/fill (Rectangle baseline is child 0 and stays).
        for (int i = GraphHost.Children.Count - 1; i >= 1; i--)
            GraphHost.Children.RemoveAt(i);

        if (buckets.Count < 2) return;

        double w = GraphHost.ActualWidth;
        double h = GraphHost.ActualHeight;
        if (w <= 0 || h <= 0) return; // Will be re-rendered on SizeChanged

        int max = buckets.Max();
        if (max == 0) return; // Only baseline shows when no activity

        const double topPad = 4;
        const double bottomPad = 1; // Sit just above the baseline rule when value = 0
        double usableHeight = Math.Max(1, h - topPad - bottomPad);

        var linePoints = new PointCollection();
        var fillPoints = new PointCollection
        {
            new Point(0, h - bottomPad), // Start at bottom-left
        };

        for (int i = 0; i < buckets.Count; i++)
        {
            double x = (double)i / (buckets.Count - 1) * w;
            double ratio = (double)buckets[i] / max;
            double y = (h - bottomPad) - ratio * usableHeight;

            linePoints.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        fillPoints.Add(new Point(w, h - bottomPad)); // Close at bottom-right

        var brandBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"];
        var fillColor = brandBrush.Color;
        fillColor.A = 40; // Subtle fill

        var fill = new Polygon
        {
            Points = fillPoints,
            Fill = new SolidColorBrush(fillColor),
            StrokeThickness = 0,
        };

        var line = new Polyline
        {
            Points = linePoints,
            Stroke = brandBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        GraphHost.Children.Add(fill);
        GraphHost.Children.Add(line);
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
