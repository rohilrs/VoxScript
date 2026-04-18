using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Onboarding;
using VoxScript.Onboarding.Steps;
using Xunit;

namespace VoxScript.Tests.Onboarding;

public class TryItStepViewModelTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }

    private static (TryItStepViewModel vm, IGlobalHotkeyEvents hotkey, IWizardEngine engine, OnboardingViewModel onboarding) Build()
    {
        var hotkey = Substitute.For<IGlobalHotkeyEvents>();
        var engine = Substitute.For<IWizardEngine>();
        engine.StartRecordingAsync(Arg.Any<ITranscriptionModel>(), Arg.Any<bool>())
              .Returns(Task.CompletedTask);
        engine.StopAndTranscribeAsync().Returns(Task.CompletedTask);
        engine.CancelRecordingAsync().Returns(Task.CompletedTask);

        var settings = new AppSettings(new InMemorySettingsStore());
        var onboarding = new OnboardingViewModel(settings);
        var vm = new TryItStepViewModel(hotkey, engine, settings, onboarding);
        return (vm, hotkey, engine, onboarding);
    }

    [Fact]
    public void Initial_sub_state_is_Idle()
    {
        var (vm, _, _, _) = Build();
        vm.SubState.Should().Be(TryItSubState.Idle);
    }

    [Fact]
    public void RecordingStarted_transitions_to_Recording()
    {
        var (vm, hotkey, _, _) = Build();
        hotkey.RecordingStartRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        vm.SubState.Should().Be(TryItSubState.Recording);
    }

    [Fact]
    public void RecordingStopped_transitions_to_Transcribing()
    {
        var (vm, hotkey, _, _) = Build();
        hotkey.RecordingStartRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        hotkey.RecordingStopRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        vm.SubState.Should().Be(TryItSubState.Transcribing);
    }

    [Fact]
    public void Successful_transcription_transitions_to_Success_and_shows_transcript()
    {
        var (vm, hotkey, _, onboarding) = Build();
        hotkey.RecordingStartRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        hotkey.RecordingStopRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        vm.SimulateTranscriptionCompleted("hello from VoxScript");

        vm.SubState.Should().Be(TryItSubState.Success);
        vm.TranscriptText.Should().Be("hello from VoxScript");
        onboarding.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void Empty_transcript_loops_back_to_Idle_with_hint()
    {
        var (vm, _, _, _) = Build();
        vm.SimulateTranscriptionCompleted(string.Empty);
        vm.SubState.Should().Be(TryItSubState.Idle);
        vm.ShowEmptyHint.Should().BeTrue();
    }

    [Fact]
    public void Whitespace_transcript_loops_back_to_Idle_with_hint()
    {
        var (vm, _, _, _) = Build();
        vm.SimulateTranscriptionCompleted("   ");
        vm.SubState.Should().Be(TryItSubState.Idle);
        vm.ShowEmptyHint.Should().BeTrue();
    }

    [Fact]
    public void SkipForNow_unlocks_step_in_onboarding()
    {
        var (vm, _, _, onboarding) = Build();
        vm.SkipForNow();
        // Walk forward to the TryIt step — SkipForNow should have unlocked it
        onboarding.UnlockMicStep();
        onboarding.UnlockModelStep();
        onboarding.GoNext(); // Welcome → MicPick
        onboarding.GoNext(); // MicPick → ModelPick
        onboarding.GoNext(); // ModelPick → Hotkeys
        onboarding.GoNext(); // Hotkeys → TryIt
        onboarding.CurrentStep.Should().Be(OnboardingStep.TryIt);
        onboarding.CanGoNext.Should().BeTrue(); // TryIt was unlocked by SkipForNow
    }

    [Fact]
    public void TryAgain_resets_to_Idle()
    {
        var (vm, _, _, _) = Build();
        vm.SimulateTranscriptionCompleted("some text");
        vm.TryAgain();
        vm.SubState.Should().Be(TryItSubState.Idle);
        vm.ShowEmptyHint.Should().BeFalse();
        vm.TranscriptText.Should().BeEmpty();
    }

    [Fact]
    public void Engine_is_called_with_suppressAutoPaste_true()
    {
        var (vm, hotkey, engine, _) = Build();
        hotkey.RecordingStartRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);

        engine.Received(1).StartRecordingAsync(Arg.Any<ITranscriptionModel>(), suppressAutoPaste: true);
    }

    [Fact]
    public void Dispose_unsubscribes_from_events()
    {
        var (vm, hotkey, engine, _) = Build();
        vm.Dispose();

        // After dispose, events must not transition state
        hotkey.RecordingStartRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        vm.SubState.Should().Be(TryItSubState.Idle);
    }

    [Fact]
    public void Cancel_while_recording_returns_to_Idle()
    {
        var (vm, hotkey, engine, _) = Build();
        hotkey.RecordingStartRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        hotkey.RecordingCancelRequested += Raise.Event<EventHandler>(hotkey, EventArgs.Empty);
        vm.SubState.Should().Be(TryItSubState.Idle);
        engine.Received(1).CancelRecordingAsync();
    }
}
