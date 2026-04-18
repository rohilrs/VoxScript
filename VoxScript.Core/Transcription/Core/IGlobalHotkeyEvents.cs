namespace VoxScript.Core.Transcription.Core;

/// <summary>
/// Interface surface for the global hotkey service — exposes the recording-control
/// events so the onboarding TryItStepViewModel can hook them without depending on
/// the Native project directly.
/// </summary>
public interface IGlobalHotkeyEvents
{
    event EventHandler? RecordingStartRequested;
    event EventHandler? RecordingStopRequested;
    event EventHandler? RecordingCancelRequested;
}
