using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Audio;
using VoxScript.Core.Settings;
using VoxScript.Onboarding;
using VoxScript.Onboarding.Steps;
using Xunit;

namespace VoxScript.Tests.Onboarding;

public class MicStepViewModelTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }

    private static (MicStepViewModel vm, IAudioCaptureService capture, OnboardingViewModel onboarding, AppSettings settings) Build()
    {
        var capture = Substitute.For<IAudioCaptureService>();
        var devices = new List<AudioDeviceInfo>
        {
            new("id-1", "Headset Mic", true),
            new("id-2", "USB Mic", false),
        };
        capture.EnumerateDevices().Returns(devices);
        capture.DefaultDevice.Returns(devices[0]);

        var settings = new AppSettings(new InMemorySettingsStore());
        var onboarding = new OnboardingViewModel(settings);
        var vm = new MicStepViewModel(capture, settings, onboarding);
        return (vm, capture, onboarding, settings);
    }

    [Fact]
    public void Devices_are_populated_on_init()
    {
        var (vm, _, _, _) = Build();
        vm.Devices.Should().HaveCount(2);
    }

    [Fact]
    public void Default_device_is_preselected()
    {
        var (vm, _, _, _) = Build();
        vm.SelectedDevice!.Id.Should().Be("id-1");
    }

    [Fact]
    public void IsNextEnabled_false_before_signal_detected()
    {
        var (vm, _, _, _) = Build();
        vm.IsNextEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsNextEnabled_true_after_SignalDetected()
    {
        var (vm, _, _, _) = Build();
        vm.SimulateSignalDetected();
        vm.IsNextEnabled.Should().BeTrue();
    }

    [Fact]
    public void SkipCheck_enables_next_without_signal()
    {
        var (vm, _, _, _) = Build();
        vm.SkipCheck();
        vm.IsNextEnabled.Should().BeTrue();
    }

    [Fact]
    public void ConfirmDevice_writes_AudioDeviceId_to_settings()
    {
        var (vm, _, _, settings) = Build();
        vm.ConfirmDevice();
        settings.AudioDeviceId.Should().Be("id-1");
    }

    [Fact]
    public void Empty_device_list_sets_NoDevicesFound_true()
    {
        var capture = Substitute.For<IAudioCaptureService>();
        capture.EnumerateDevices().Returns(new List<AudioDeviceInfo>());
        capture.DefaultDevice.Returns((AudioDeviceInfo?)null);
        var settings = new AppSettings(new InMemorySettingsStore());
        var onboarding = new OnboardingViewModel(settings);
        var vm = new MicStepViewModel(capture, settings, onboarding);
        vm.NoDevicesFound.Should().BeTrue();
        vm.IsNextEnabled.Should().BeFalse();
    }

    [Fact]
    public void NoSignalHint_visible_after_timeout()
    {
        var (vm, _, _, _) = Build();
        vm.SimulateNoSignalTimeout();
        vm.ShowNoSignalHint.Should().BeTrue();
    }

    [Fact]
    public void OnAudioLevel_accumulates_above_threshold_and_marks_signal()
    {
        var (vm, _, onboarding, _) = Build();
        // 20 × 0.15s chunks above threshold = 3s of signal, well past 2s gate
        for (int i = 0; i < 20; i++)
            vm.OnAudioLevel(0.1f, 0.15);
        vm.SignalDetected.Should().BeTrue();
        vm.IsNextEnabled.Should().BeTrue();
        onboarding.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public void OnAudioLevel_accumulates_across_silence_gaps()
    {
        // Natural speech has brief gaps between syllables. The gate should
        // treat total time above threshold as the signal accumulator, not
        // strictly continuous time — otherwise "hello from VoxScript" never
        // trips because of its inter-word dips.
        var (vm, _, _, _) = Build();
        // 1s of signal, 0.5s silence, 1.1s more signal = 2.1s total above threshold
        for (int i = 0; i < 10; i++) vm.OnAudioLevel(0.1f, 0.1);
        for (int i = 0; i < 5; i++)  vm.OnAudioLevel(0.0f, 0.1);
        for (int i = 0; i < 11; i++) vm.OnAudioLevel(0.1f, 0.1);
        vm.SignalDetected.Should().BeTrue();
    }

    [Fact]
    public void Retry_reloads_devices_after_empty()
    {
        var capture = Substitute.For<IAudioCaptureService>();
        capture.EnumerateDevices().Returns(
            new List<AudioDeviceInfo>(),
            new List<AudioDeviceInfo> { new("id-1", "Mic", true) }
        );
        capture.DefaultDevice.Returns((AudioDeviceInfo?)null, new AudioDeviceInfo("id-1", "Mic", true));
        var settings = new AppSettings(new InMemorySettingsStore());
        var onboarding = new OnboardingViewModel(settings);
        var vm = new MicStepViewModel(capture, settings, onboarding);
        vm.NoDevicesFound.Should().BeTrue();

        vm.Retry();
        vm.NoDevicesFound.Should().BeFalse();
        vm.SelectedDevice!.Id.Should().Be("id-1");
    }
}
