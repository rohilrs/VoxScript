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
        if (w <= 0 || h <= 0) return;

        // Belt-and-suspenders: clip any drawing to the host's bounds so a Bezier
        // curve that overshoots the top can't paint into the card's header area.
        GraphHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) };

        int max = buckets.Max();
        if (max == 0) return;

        const double topPad = 4;
        const double bottomPad = 1;
        double usableHeight = Math.Max(1, h - topPad - bottomPad);
        double baseline = h - bottomPad;

        var pts = new Point[buckets.Count];
        for (int i = 0; i < buckets.Count; i++)
        {
            double x = (double)i / (buckets.Count - 1) * w;
            double ratio = (double)buckets[i] / max;
            double y = baseline - ratio * usableHeight;
            pts[i] = new Point(x, y);
        }

        var brandBrush = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"];
        var fillColor = brandBrush.Color;
        fillColor.A = 40;

        // Line path
        var lineFigure = new PathFigure { StartPoint = pts[0], IsClosed = false };
        AppendSmoothSegments(lineFigure, pts, topPad, baseline);
        var line = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = { lineFigure } },
            Stroke = brandBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        // Fill path: baseline-left → first point → smoothed curve → baseline-right → close
        var fillFigure = new PathFigure { StartPoint = new Point(0, baseline), IsClosed = true };
        fillFigure.Segments.Add(new LineSegment { Point = pts[0] });
        AppendSmoothSegments(fillFigure, pts, topPad, baseline);
        fillFigure.Segments.Add(new LineSegment { Point = new Point(w, baseline) });
        var fill = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = { fillFigure } },
            Fill = new SolidColorBrush(fillColor),
        };

        GraphHost.Children.Add(fill);
        GraphHost.Children.Add(line);
    }

    /// <summary>
    /// Append Catmull-Rom → cubic Bezier segments to the figure. The curve passes through
    /// every point in <paramref name="pts"/>. Control-point Y is clamped to
    /// [<paramref name="topPad"/>, <paramref name="baseline"/>] so the curve can't overshoot
    /// above the top of the chart or dip below the baseline between sharp data spikes —
    /// a cubic Bezier is contained in the convex hull of its 4 control points, so clamping
    /// all 4 into the band keeps the whole curve in.
    /// </summary>
    private static void AppendSmoothSegments(PathFigure figure, Point[] pts, double topPad, double baseline)
    {
        // Tension 0.5 — softer than standard Catmull-Rom (1.0) to reduce overshoot on sparse data.
        const double tension = 0.5;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Point p0 = i > 0 ? pts[i - 1] : pts[i];
            Point p1 = pts[i];
            Point p2 = pts[i + 1];
            Point p3 = i + 2 < pts.Length ? pts[i + 2] : pts[i + 1];

            double c1x = p1.X + (p2.X - p0.X) * tension / 3.0;
            double c1y = p1.Y + (p2.Y - p0.Y) * tension / 3.0;
            double c2x = p2.X - (p3.X - p1.X) * tension / 3.0;
            double c2y = p2.Y - (p3.Y - p1.Y) * tension / 3.0;

            // Clamp control points into the chart band so the curve can't
            // overshoot above the top (cuts into the card header) or dip
            // below the baseline.
            if (c1y > baseline) c1y = baseline;
            if (c1y < topPad) c1y = topPad;
            if (c2y > baseline) c2y = baseline;
            if (c2y < topPad) c2y = topPad;

            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(c1x, c1y),
                Point2 = new Point(c2x, c2y),
                Point3 = p2,
            });
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
