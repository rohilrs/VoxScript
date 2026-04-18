using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

/// <summary>
/// Narrow abstraction over <see cref="VoxScriptEngine"/> for the onboarding
/// TryItStepViewModel. Keeps the VM mockable without exposing the full engine surface.
/// </summary>
public interface IWizardEngine
{
    Task StartRecordingAsync(ITranscriptionModel model, bool suppressAutoPaste = false);
    Task StopAndTranscribeAsync();
    Task CancelRecordingAsync();
    RecordingState State { get; }
    event EventHandler<string>? TranscriptionCompleted;
}
