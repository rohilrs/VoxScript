using FluentAssertions;
using VoxScript.Core.Settings;
using VoxScript.Onboarding;
using Xunit;

namespace VoxScript.Tests.Onboarding;

public class OnboardingViewModelTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }

    private static AppSettings MakeSettings() => new(new InMemorySettingsStore());

    [Fact]
    public void Initial_step_is_Welcome()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.CurrentStep.Should().Be(OnboardingStep.Welcome);
    }

    [Fact]
    public void Welcome_has_no_back_button()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void GoNext_from_Welcome_advances_to_MicPick()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.GoNext();
        vm.CurrentStep.Should().Be(OnboardingStep.MicPick);
    }

    [Fact]
    public void GoBack_from_MicPick_returns_to_Welcome()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.GoNext();
        vm.GoBack();
        vm.CurrentStep.Should().Be(OnboardingStep.Welcome);
    }

    [Fact]
    public void Full_forward_navigation_reaches_Final()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.UnlockMicStep();
        vm.UnlockModelStep();
        vm.UnlockTryItStep();

        vm.GoNext(); // Welcome → MicPick
        vm.GoNext(); // MicPick → ModelPick
        vm.GoNext(); // ModelPick → Hotkeys
        vm.GoNext(); // Hotkeys → TryIt
        vm.GoNext(); // TryIt → Final
        vm.CurrentStep.Should().Be(OnboardingStep.Final);
    }

    [Fact]
    public void GoNext_is_blocked_when_step_is_gated()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.GoNext(); // → MicPick (gated by default)
        var stepBefore = vm.CurrentStep;
        vm.GoNext(); // should not advance because MicPick is gated
        vm.CurrentStep.Should().Be(stepBefore);
    }

    [Fact]
    public void Finish_writes_OnboardingCompleted_true()
    {
        var settings = MakeSettings();
        var vm = new OnboardingViewModel(settings);
        vm.Finish();
        settings.OnboardingCompleted.Should().BeTrue();
    }

    [Fact]
    public void Finish_raises_WizardCompleted_event()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        bool raised = false;
        vm.WizardCompleted += () => raised = true;
        vm.Finish();
        raised.Should().BeTrue();
    }

    [Fact]
    public void OnboardingCompleted_is_not_set_mid_wizard()
    {
        var settings = MakeSettings();
        var vm = new OnboardingViewModel(settings);
        vm.GoNext(); // Welcome → MicPick
        settings.OnboardingCompleted.Should().BeNull();
    }

    [Fact]
    public void GoNext_past_Final_does_nothing()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.UnlockMicStep();
        vm.UnlockModelStep();
        vm.UnlockTryItStep();
        for (int i = 0; i < 5; i++) vm.GoNext();
        vm.CurrentStep.Should().Be(OnboardingStep.Final);
        vm.GoNext(); // one too many
        vm.CurrentStep.Should().Be(OnboardingStep.Final);
    }

    [Fact]
    public void CanGoNext_flips_when_step_is_unlocked()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.GoNext(); // → MicPick (gated)
        vm.CanGoNext.Should().BeFalse();
        vm.UnlockMicStep();
        vm.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void ProgressValue_reflects_current_step()
    {
        var vm = new OnboardingViewModel(MakeSettings());
        vm.ProgressValue.Should().BeApproximately(1.0 / 6.0, 0.001);
        vm.UnlockMicStep();
        vm.GoNext(); // MicPick (2/6)
        vm.ProgressValue.Should().BeApproximately(2.0 / 6.0, 0.001);
    }
}
