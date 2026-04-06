using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Settings;
using Xunit;

namespace VoxScript.Tests.Settings;

public class AppSettingsTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }

    [Fact]
    public void AppSettings_AiEnhancementEnabled_defaults_false()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.AiEnhancementEnabled.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_roundtrips_AudioDeviceId()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.AudioDeviceId = "device-123";
        settings.AudioDeviceId.Should().Be("device-123");
    }

    [Fact]
    public void AppSettings_SoundEffectsEnabled_defaults_true()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.SoundEffectsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_LaunchAtLogin_defaults_true()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.LaunchAtLogin.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_MinimizeToTray_defaults_true()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.MinimizeToTray.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_SmartFormattingEnabled_defaults_true()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.SmartFormattingEnabled.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_RecordingIndicatorMode_defaults_DuringRecording()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.RecordingIndicatorMode.Should().Be(RecordingIndicatorMode.DuringRecording);
    }

    [Fact]
    public void AppSettings_HoldHotkey_defaults_to_CtrlWin()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.HoldHotkey.Should().Be("Ctrl+Win");
    }

    [Fact]
    public void AppSettings_ToggleHotkey_defaults_to_CtrlWinSpace()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.ToggleHotkey.Should().Be("Ctrl+Win+Space");
    }

    [Fact]
    public void AppSettings_roundtrips_ToggleHotkey()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.ToggleHotkey = "Alt+T";
        settings.ToggleHotkey.Should().Be("Alt+T");
    }
}
