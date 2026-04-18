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
    event EventHandler? RecordingToggleRequested;
    event EventHandler? RecordingCancelRequested;
    /// <summary>
    /// Fires when Space converts a hold into a toggle-locked recording. This is
    /// the *only* event the service fires for hold→toggle promotion — no separate
    /// RecordingToggleRequested fires in that path.
    /// </summary>
    event Action? ToggleLockActivated;
}
