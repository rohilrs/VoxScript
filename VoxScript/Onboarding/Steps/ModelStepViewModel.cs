using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Native.Whisper;

namespace VoxScript.Onboarding.Steps;

public enum ModelSubState { Picker, Downloading, Done, Failed }

public sealed partial class ModelStepViewModel : ObservableObject
{
    private readonly IWhisperModelManager _manager;
    private readonly ILocalTranscriptionBackend _backend;
    private readonly AppSettings _settings;
    private readonly OnboardingViewModel _onboarding;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    public partial ModelSubState SubState { get; private set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial int SelectedChoiceIndex { get; set; }

    public static readonly (TranscriptionModel Model, string Label, string Description)[] Choices =
    [
        (PredefinedModels.TinyEn,       "Fast",     "Quickest download, works on any machine. Good for short phrases."),
        (PredefinedModels.BaseEn,       "Balanced", "Solid accuracy on any hardware. The best default for most users."),
        (PredefinedModels.LargeV3Turbo, "Accurate", "Highest accuracy. Best with a GPU (Vulkan is bundled)."),
    ];

    public ModelStepViewModel(
        IWhisperModelManager manager,
        ILocalTranscriptionBackend backend,
        AppSettings settings,
        OnboardingViewModel onboarding)
    {
        _manager = manager;
        _backend = backend;
        _settings = settings;
        _onboarding = onboarding;
        SubState = ModelSubState.Picker;
        SelectedChoiceIndex = 1; // Balanced pre-selected
    }

    public async Task StartDownloadAsync()
    {
        if (_downloadCts is not null) return; // guard against re-entrancy (rapid Retry clicks, double-fire)
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        var model = Choices[SelectedChoiceIndex].Model;

        SubState = ModelSubState.Downloading;
        DownloadProgress = 0;
        ErrorMessage = null;

        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);

            await _manager.DownloadAsync(model.Name, progress, ct);

            _ = Task.Run(async () =>
            {
                try { await _manager.DownloadVadAsync(null, CancellationToken.None); }
                catch (Exception ex) { Log.Warning(ex, "Onboarding: VAD download failed (non-blocking)"); }
            });

            var modelPath = _manager.GetModelPath(model.Name);
            await _backend.LoadModelAsync(modelPath, ct);

            _settings.SelectedModelName = model.Name;
            SubState = ModelSubState.Done;
            _onboarding.UnlockModelStep();
        }
        catch (OperationCanceledException)
        {
            try { _manager.DeleteModel(model.Name); } catch { /* best-effort cleanup */ }
            SubState = ModelSubState.Picker;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Onboarding: model download/load failed");
            ErrorMessage = ex.Message;
            SubState = ModelSubState.Failed;
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    public void CancelDownload()
    {
        // Capture locally — the finally in StartDownloadAsync may Dispose this concurrently.
        var cts = _downloadCts;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void ReturnToPicker()
    {
        _onboarding.SetStepGated(OnboardingStep.ModelPick, true);
        SubState = ModelSubState.Picker;
    }
}
