// VoxScript/Views/TranscribePage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Infrastructure;
using VoxScript.Core.AI;

namespace VoxScript.Views;

public sealed partial class TranscribePage : Page
{
    private readonly VoxScriptEngine _engine;
    private readonly TranscriptionPipeline _pipeline;
    private string _lastTranscription = "";

    public TranscribePage()
    {
        this.InitializeComponent();
        _engine = ServiceLocator.Get<VoxScriptEngine>();
        _pipeline = ServiceLocator.Get<TranscriptionPipeline>();

        _engine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoxScriptEngine.State))
                DispatcherQueue.TryEnqueue(() => OnStateChanged(_engine.State));
        };
        _engine.TranscriptionCompleted += (_, text) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _lastTranscription = text;
                TranscriptText.Text = text;
                TranscriptText.Foreground = (SolidColorBrush)Application.Current.Resources["BrandForegroundBrush"];
                UpdateContextBadge();
            });
        };
        _engine.TranscriptionFailed += (_, err) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TranscriptText.Text = $"Error: {err}";
                TranscriptText.Foreground = (SolidColorBrush)Application.Current.Resources["BrandRecordingBrush"];
            });
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var model = ResolveModel();
        ModelName.Text = model.DisplayName;
    }

    private void OnStateChanged(RecordingState state)
    {
        switch (state)
        {
            case RecordingState.Recording:
                RecordButton.Background = (SolidColorBrush)Application.Current.Resources["BrandRecordingBrush"];
                RecordHint.Text = "Tap to stop recording";
                StatusDot.Fill = (SolidColorBrush)Application.Current.Resources["BrandRecordingBrush"];
                StatusText.Text = "Recording";
                break;
            case RecordingState.Transcribing:
                StatusText.Text = "Transcribing...";
                StatusDot.Fill = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"];
                RecordButton.Background = (SolidColorBrush)Application.Current.Resources["BrandMutedBrush"];
                RecordHint.Text = "";
                break;
            case RecordingState.Enhancing:
                StatusText.Text = "Enhancing...";
                break;
            default: // Idle
                RecordButton.Background = (SolidColorBrush)Application.Current.Resources["BrandPrimaryBrush"];
                RecordHint.Text = "Tap to start recording";
                StatusDot.Fill = (SolidColorBrush)Application.Current.Resources["BrandSuccessBrush"];
                StatusText.Text = "Ready";
                break;
        }
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        var model = ResolveModel();
        ModelName.Text = model.DisplayName;
        await _engine.ToggleRecordAsync(model);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastTranscription))
        {
            var package = new DataPackage();
            package.SetText(_lastTranscription);
            Clipboard.SetContent(package);
        }
    }

    private void UpdateContextBadge()
    {
        var modeName = _pipeline.LastMatchedModeName;
        var processName = _pipeline.LastMatchedProcessName;
        if (modeName is not null)
        {
            var label = processName is not null ? $"{modeName} — {processName}" : modeName;
            ContextModeBadge.Text = label;
            ContextModeBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ContextModeBadge.Visibility = Visibility.Collapsed;
        }
    }

    private static ITranscriptionModel ResolveModel()
    {
        var settings = ServiceLocator.Get<AppSettings>();
        var modelName = settings.SelectedModelName;
        return (modelName is not null
            ? PredefinedModels.All.FirstOrDefault(m => m.Name == modelName)
            : null) ?? PredefinedModels.Default;
    }
}
