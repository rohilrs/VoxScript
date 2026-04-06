# Settings Page Implementation Plan

## Goal

Replace the placeholder SettingsPage with a fully functional settings UI matching the approved design spec. Five grouped cards (Keybinds, Input, App, Sound, Extras) with auto-saving, inline keybind recording, and disabled-feature dimming.

## Architecture

- **SettingsViewModel** (CommunityToolkit.Mvvm `ObservableObject`) lives in `VoxScript/ViewModels/` and owns all settings state, keybind recording logic, and audio device enumeration.
- **HotkeySerializer** is a static utility in `VoxScript.Core/Settings/` that converts between `string` ("Ctrl+Win+Space") and `HotkeyCombo` / display text.
- **AppSettings** gains new properties for all settings in the spec.
- **SettingsPage.xaml** is rewritten with 5 card sections; code-behind is thin (KeyDown/KeyUp forwarding only).
- Auto-save: every property setter in the ViewModel writes through to `AppSettings` immediately.
- No new DI registrations needed for the ViewModel (it is instantiated directly by `SettingsPage` via `ServiceLocator`, same pattern as `TranscribePage`).

## Tech Stack

- .NET 10, WinUI 3 (WindowsAppSDK)
- CommunityToolkit.Mvvm 8.4.2 (already referenced in VoxScript.Core and VoxScript projects)
- xUnit + FluentAssertions + NSubstitute (existing test stack)

---

## Task 1: Add new AppSettings properties

- [ ] **Modify** `VoxScript.Core/Settings/AppSettings.cs`

Add the following properties after the existing `PrimaryHotkeyMode` property:

```csharp
    public bool LaunchAtLogin
    {
        get => _store.Get<bool?>(nameof(LaunchAtLogin)) ?? true;
        set => _store.Set(nameof(LaunchAtLogin), value);
    }

    public bool MinimizeToTray
    {
        get => _store.Get<bool?>(nameof(MinimizeToTray)) ?? true;
        set => _store.Set(nameof(MinimizeToTray), value);
    }

    public bool SmartFormattingEnabled
    {
        get => _store.Get<bool?>(nameof(SmartFormattingEnabled)) ?? true;
        set => _store.Set(nameof(SmartFormattingEnabled), value);
    }

    public bool RecordingIndicatorEnabled
    {
        get => _store.Get<bool?>(nameof(RecordingIndicatorEnabled)) ?? false;
        set => _store.Set(nameof(RecordingIndicatorEnabled), value);
    }

    public bool PauseMediaWhileDictating
    {
        get => _store.Get<bool?>(nameof(PauseMediaWhileDictating)) ?? false;
        set => _store.Set(nameof(PauseMediaWhileDictating), value);
    }

    public bool AutoAddToDictionary
    {
        get => _store.Get<bool?>(nameof(AutoAddToDictionary)) ?? false;
        set => _store.Set(nameof(AutoAddToDictionary), value);
    }

    public string HoldHotkey
    {
        get => _store.Get<string>(nameof(HoldHotkey)) ?? "Ctrl+Win";
        set => _store.Set(nameof(HoldHotkey), value);
    }

    public string ToggleHotkey
    {
        get => _store.Get<string>(nameof(ToggleHotkey)) ?? "Ctrl+Win+Space";
        set => _store.Set(nameof(ToggleHotkey), value);
    }

    public string PasteLastHotkey
    {
        get => _store.Get<string>(nameof(PasteLastHotkey)) ?? "Alt+Shift+Z";
        set => _store.Set(nameof(PasteLastHotkey), value);
    }

    public string CancelHotkey
    {
        get => _store.Get<string>(nameof(CancelHotkey)) ?? "Esc";
        set => _store.Set(nameof(CancelHotkey), value);
    }
```

- [ ] **Modify** `VoxScript.Tests/Settings/AppSettingsTests.cs`

Add tests for the new properties:

```csharp
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
    public void AppSettings_RecordingIndicatorEnabled_defaults_false()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        settings.RecordingIndicatorEnabled.Should().BeFalse();
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
```

**Verify:**
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
"/mnt/c/Program Files/dotnet/dotnet.exe" test VoxScript.Tests
```

---

## Task 2: Create HotkeySerializer utility

- [ ] **Create** `VoxScript.Core/Settings/HotkeySerializer.cs`

This static class converts between the persisted string format ("Ctrl+Win+Space") and the `HotkeyCombo` record used by `GlobalHotkeyService`. It also provides display-friendly formatting.

```csharp
using VoxScript.Native.Platform;

namespace VoxScript.Core.Settings;

/// <summary>
/// Converts between hotkey string representation ("Ctrl+Win+Space")
/// and HotkeyCombo (ModifierKeys + virtual key code).
/// </summary>
public static class HotkeySerializer
{
    // Common virtual key codes for trigger keys
    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["Enter"] = 0x0D,
        ["Tab"] = 0x09,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Up"] = 0x26,
        ["Down"] = 0x28,
        ["Left"] = 0x25,
        ["Right"] = 0x27,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    private static readonly Dictionary<int, string> VkToName =
        NameToVk.GroupBy(kv => kv.Value)
                 .ToDictionary(g => g.Key, g => g.First().Key);

    private static readonly HashSet<string> ModifierNames =
        new(StringComparer.OrdinalIgnoreCase) { "Ctrl", "Shift", "Alt", "Win" };

    /// <summary>
    /// Parse a hotkey string like "Ctrl+Win+Space" into a HotkeyCombo.
    /// Returns null if the string is null, empty, or "Not set".
    /// </summary>
    public static HotkeyCombo? Parse(string? hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString) || hotkeyString == "Not set")
            return null;

        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        int? triggerKey = null;

        foreach (var part in parts)
        {
            if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Ctrl;
            else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
            else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Win;
            else if (NameToVk.TryGetValue(part, out var vk))
                triggerKey = vk;
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                triggerKey = char.ToUpper(part[0]); // A-Z, 0-9 VK codes match ASCII
        }

        return new HotkeyCombo(modifiers, triggerKey);
    }

    /// <summary>
    /// Serialize a HotkeyCombo back to display string like "Ctrl+Win+Space".
    /// </summary>
    public static string Serialize(HotkeyCombo? combo)
    {
        if (combo is null) return "Not set";

        var parts = new List<string>();
        if (combo.Modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
        if (combo.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (combo.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (combo.Modifiers.HasFlag(ModifierKeys.Win)) parts.Add("Win");

        if (combo.TriggerKey is int vk)
        {
            if (VkToName.TryGetValue(vk, out var name))
                parts.Add(name);
            else if (vk is >= 0x30 and <= 0x5A) // 0-9, A-Z
                parts.Add(((char)vk).ToString());
            else
                parts.Add($"0x{vk:X2}");
        }

        return parts.Count > 0 ? string.Join("+", parts) : "Not set";
    }

    /// <summary>
    /// Build a HotkeyCombo from WinUI virtual key values captured during recording.
    /// </summary>
    public static HotkeyCombo FromVirtualKeys(ModifierKeys modifiers, int? triggerVk)
    {
        return new HotkeyCombo(modifiers, triggerVk);
    }

    /// <summary>
    /// Check if a virtual key code is a modifier key (not a trigger).
    /// </summary>
    public static bool IsModifierKey(int vkCode)
    {
        return vkCode is
            0xA0 or 0xA1 or 0x10 or  // Shift (L, R, generic)
            0xA2 or 0xA3 or 0x11 or  // Ctrl (L, R, generic)
            0xA4 or 0xA5 or 0x12 or  // Alt (L, R, generic)
            0x5B or 0x5C;             // Win (L, R)
    }

    /// <summary>
    /// Get the ModifierKeys flag for a virtual key code, or None if not a modifier.
    /// </summary>
    public static ModifierKeys VkToModifier(int vkCode)
    {
        return vkCode switch
        {
            0xA0 or 0xA1 or 0x10 => ModifierKeys.Shift,
            0xA2 or 0xA3 or 0x11 => ModifierKeys.Ctrl,
            0xA4 or 0xA5 or 0x12 => ModifierKeys.Alt,
            0x5B or 0x5C => ModifierKeys.Win,
            _ => ModifierKeys.None,
        };
    }
}
```

Note: `VoxScript.Core` does not reference `VoxScript.Native` (the dependency flows the other way). The `HotkeyCombo` and `ModifierKeys` types are defined in `VoxScript.Native/Platform/GlobalHotkeyService.cs`. We need to move these types or create the serializer in a project that can see both.

**Resolution:** Place `HotkeySerializer` in `VoxScript/Helpers/HotkeySerializer.cs` instead (the UI project references both Core and Native). Update the namespace to `VoxScript.Helpers`.

- [ ] **Create** `VoxScript/Helpers/HotkeySerializer.cs`

Same code as above, but with namespace `VoxScript.Helpers` and the correct `using VoxScript.Native.Platform;`.

- [ ] **Create** `VoxScript.Tests/Settings/HotkeySerializerTests.cs`

```csharp
using FluentAssertions;
using VoxScript.Helpers;
using VoxScript.Native.Platform;
using Xunit;

namespace VoxScript.Tests.Settings;

public class HotkeySerializerTests
{
    [Theory]
    [InlineData("Ctrl+Win+Space", ModifierKeys.Ctrl | ModifierKeys.Win, 0x20)]
    [InlineData("Ctrl+Win", ModifierKeys.Ctrl | ModifierKeys.Win, null)]
    [InlineData("Alt+Shift+Z", ModifierKeys.Alt | ModifierKeys.Shift, (int)'Z')]
    [InlineData("Esc", ModifierKeys.None, 0x1B)]
    public void Parse_produces_correct_combo(string input, ModifierKeys expectedMods, int? expectedTrigger)
    {
        var combo = HotkeySerializer.Parse(input);
        combo.Should().NotBeNull();
        combo!.Modifiers.Should().Be(expectedMods);
        combo.TriggerKey.Should().Be(expectedTrigger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not set")]
    public void Parse_returns_null_for_empty_or_notset(string? input)
    {
        HotkeySerializer.Parse(input).Should().BeNull();
    }

    [Fact]
    public void Serialize_roundtrips_CtrlWinSpace()
    {
        var combo = new HotkeyCombo(ModifierKeys.Ctrl | ModifierKeys.Win, 0x20);
        var str = HotkeySerializer.Serialize(combo);
        str.Should().Be("Ctrl+Win+Space");
    }

    [Fact]
    public void Serialize_null_returns_NotSet()
    {
        HotkeySerializer.Serialize(null).Should().Be("Not set");
    }

    [Fact]
    public void Serialize_modifierOnly_combo()
    {
        var combo = new HotkeyCombo(ModifierKeys.Ctrl | ModifierKeys.Win, null);
        HotkeySerializer.Serialize(combo).Should().Be("Ctrl+Win");
    }

    [Theory]
    [InlineData(0xA2, true)]   // Left Ctrl
    [InlineData(0x5B, true)]   // Left Win
    [InlineData(0x20, false)]  // Space
    [InlineData(0x41, false)]  // A
    public void IsModifierKey_classifies_correctly(int vk, bool expected)
    {
        HotkeySerializer.IsModifierKey(vk).Should().Be(expected);
    }
}
```

Note: The test project needs to reference the `VoxScript` UI project to access `VoxScript.Helpers.HotkeySerializer`. Check existing test project references first. If the test project doesn't reference `VoxScript`, add it -- or alternatively, check if we can test via the public types. Since the test project already references `VoxScript.Native` (for `GlobalHotkeyLogicTests`), we just need to also add `VoxScript` project reference.

- [ ] **Modify** `VoxScript.Tests/VoxScript.Tests.csproj` -- add project reference to VoxScript if not already present:

```xml
    <ProjectReference Include="..\VoxScript\VoxScript.csproj" />
```

**Verify:**
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
"/mnt/c/Program Files/dotnet/dotnet.exe" test VoxScript.Tests
```

---

## Task 3: Create SettingsViewModel

- [ ] **Create** `VoxScript/ViewModels/SettingsViewModel.cs`

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxScript.Core.Audio;
using VoxScript.Core.Settings;
using VoxScript.Helpers;
using VoxScript.Infrastructure;
using VoxScript.Native.Platform;

namespace VoxScript.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IAudioCaptureService _audioService;
    private readonly GlobalHotkeyService _hotkeyService;

    public SettingsViewModel()
    {
        _settings = ServiceLocator.Get<AppSettings>();
        _audioService = ServiceLocator.Get<IAudioCaptureService>();
        _hotkeyService = ServiceLocator.Get<GlobalHotkeyService>();

        LoadAudioDevices();
        LoadSettings();
    }

    // ── Audio Devices ──────────────────────────────────────────

    public ObservableCollection<AudioDeviceDisplay> AudioDevices { get; } = new();

    [ObservableProperty]
    private AudioDeviceDisplay? _selectedAudioDevice;

    partial void OnSelectedAudioDeviceChanged(AudioDeviceDisplay? value)
    {
        if (value is null) return;
        _settings.AudioDeviceId = value.IsDefault ? null : value.Id;
    }

    private void LoadAudioDevices()
    {
        AudioDevices.Clear();
        AudioDevices.Add(new AudioDeviceDisplay(null, "System default (auto-detect)", true));

        foreach (var device in _audioService.EnumerateDevices())
        {
            AudioDevices.Add(new AudioDeviceDisplay(device.Id, device.DisplayName, false));
        }
    }

    // ── Language ────────────────────────────────────────────────

    public static IReadOnlyList<string> Languages { get; } = new[]
    {
        "English", "Spanish", "French", "German", "Italian", "Portuguese",
        "Dutch", "Russian", "Chinese", "Japanese", "Korean", "Arabic",
        "Hindi", "Turkish", "Polish", "Ukrainian", "Czech", "Danish",
        "Finnish", "Greek", "Hebrew", "Hungarian", "Indonesian", "Malay",
        "Norwegian", "Romanian", "Swedish", "Thai", "Vietnamese",
    };

    [ObservableProperty]
    private string _selectedLanguage = "English";

    partial void OnSelectedLanguageChanged(string value)
    {
        _settings.TranscriptionLanguage = value;
    }

    // ── Toggle settings (auto-save on change) ──────────────────

    [ObservableProperty]
    private bool _launchAtLogin;
    partial void OnLaunchAtLoginChanged(bool value) => _settings.LaunchAtLogin = value;

    [ObservableProperty]
    private bool _minimizeToTray;
    partial void OnMinimizeToTrayChanged(bool value) => _settings.MinimizeToTray = value;

    [ObservableProperty]
    private bool _smartFormattingEnabled;
    partial void OnSmartFormattingEnabledChanged(bool value) => _settings.SmartFormattingEnabled = value;

    [ObservableProperty]
    private bool _soundEffectsEnabled;
    partial void OnSoundEffectsEnabledChanged(bool value) => _settings.SoundEffectsEnabled = value;

    // Disabled features (stored but UI is non-interactive)
    [ObservableProperty]
    private bool _recordingIndicatorEnabled;

    [ObservableProperty]
    private bool _pauseMediaWhileDictating;

    [ObservableProperty]
    private bool _autoAddToDictionary;

    // ── Keybinds ───────────────────────────────────────────────

    [ObservableProperty]
    private string _holdHotkeyDisplay = "Ctrl+Win";

    [ObservableProperty]
    private string _toggleHotkeyDisplay = "Ctrl+Win+Space";

    [ObservableProperty]
    private string _pasteLastHotkeyDisplay = "Alt+Shift+Z";

    [ObservableProperty]
    private string _cancelHotkeyDisplay = "Esc";

    // Recording state
    [ObservableProperty]
    private bool _isRecordingKeybind;

    [ObservableProperty]
    private string? _recordingSlot;

    [ObservableProperty]
    private string _recordingPreview = "";

    // Tracks the value before recording started, for revert on Escape
    private string _preRecordValue = "";

    // Tracks live modifiers during recording
    private ModifierKeys _recordingModifiers = ModifierKeys.None;

    public void StartRecordingKeybind(string slot)
    {
        _preRecordValue = slot switch
        {
            "HoldHotkey" => HoldHotkeyDisplay,
            "ToggleHotkey" => ToggleHotkeyDisplay,
            "PasteLastHotkey" => PasteLastHotkeyDisplay,
            "CancelHotkey" => CancelHotkeyDisplay,
            _ => ""
        };

        RecordingSlot = slot;
        IsRecordingKeybind = true;
        RecordingPreview = "Press keys...";
        _recordingModifiers = ModifierKeys.None;

        // Unregister global hotkeys to prevent interference
        _hotkeyService.Unregister();
    }

    /// <summary>
    /// Called from Page KeyDown handler. Returns true if the event was consumed.
    /// </summary>
    public bool HandleKeyDown(int vkCode)
    {
        if (!IsRecordingKeybind) return false;

        // Escape: revert and exit
        if (vkCode == 0x1B)
        {
            CancelRecording();
            return true;
        }

        // Backspace: clear binding
        if (vkCode == 0x08)
        {
            CommitKeybind("Not set");
            return true;
        }

        if (HotkeySerializer.IsModifierKey(vkCode))
        {
            _recordingModifiers |= HotkeySerializer.VkToModifier(vkCode);
            RecordingPreview = HotkeySerializer.Serialize(
                new HotkeyCombo(_recordingModifiers, null));
            return true;
        }

        // Non-modifier key pressed: this is the trigger key, commit
        var combo = new HotkeyCombo(_recordingModifiers, vkCode);
        CommitKeybind(HotkeySerializer.Serialize(combo));
        return true;
    }

    /// <summary>
    /// Called from Page KeyUp handler. Handles modifier-only combos
    /// (all modifiers released with no trigger key).
    /// </summary>
    public bool HandleKeyUp(int vkCode)
    {
        if (!IsRecordingKeybind) return false;

        if (HotkeySerializer.IsModifierKey(vkCode))
        {
            // Check if this was a modifier-only combo being released
            // Only commit if we had modifiers and this release clears them
            // We don't track individual releases -- just commit the current modifiers
            // when any modifier is released (the combo is "what was held")
            if (_recordingModifiers != ModifierKeys.None)
            {
                var combo = new HotkeyCombo(_recordingModifiers, null);
                CommitKeybind(HotkeySerializer.Serialize(combo));
            }
            return true;
        }

        return false;
    }

    private void CommitKeybind(string displayValue)
    {
        switch (RecordingSlot)
        {
            case "HoldHotkey":
                HoldHotkeyDisplay = displayValue;
                _settings.HoldHotkey = displayValue;
                break;
            case "ToggleHotkey":
                ToggleHotkeyDisplay = displayValue;
                _settings.ToggleHotkey = displayValue;
                break;
            case "PasteLastHotkey":
                PasteLastHotkeyDisplay = displayValue;
                _settings.PasteLastHotkey = displayValue;
                break;
            case "CancelHotkey":
                CancelHotkeyDisplay = displayValue;
                _settings.CancelHotkey = displayValue;
                break;
        }

        ExitRecordingMode();
        ApplyHotkeysToService();
    }

    private void CancelRecording()
    {
        // Revert display to pre-record value (no settings change)
        switch (RecordingSlot)
        {
            case "HoldHotkey": HoldHotkeyDisplay = _preRecordValue; break;
            case "ToggleHotkey": ToggleHotkeyDisplay = _preRecordValue; break;
            case "PasteLastHotkey": PasteLastHotkeyDisplay = _preRecordValue; break;
            case "CancelHotkey": CancelHotkeyDisplay = _preRecordValue; break;
        }

        ExitRecordingMode();
    }

    private void ExitRecordingMode()
    {
        IsRecordingKeybind = false;
        RecordingSlot = null;
        RecordingPreview = "";
        _recordingModifiers = ModifierKeys.None;

        // Re-register global hotkeys
        _hotkeyService.Register();
    }

    private void ApplyHotkeysToService()
    {
        var holdCombo = HotkeySerializer.Parse(_settings.HoldHotkey);
        var toggleCombo = HotkeySerializer.Parse(_settings.ToggleHotkey);

        if (holdCombo is not null)
            _hotkeyService.SetHoldHotkey(holdCombo.Modifiers, holdCombo.TriggerKey);
        if (toggleCombo is not null)
            _hotkeyService.SetToggleHotkey(toggleCombo.Modifiers, toggleCombo.TriggerKey);
    }

    // ── Load from persisted settings ───────────────────────────

    private void LoadSettings()
    {
        LaunchAtLogin = _settings.LaunchAtLogin;
        MinimizeToTray = _settings.MinimizeToTray;
        SmartFormattingEnabled = _settings.SmartFormattingEnabled;
        SoundEffectsEnabled = _settings.SoundEffectsEnabled;
        RecordingIndicatorEnabled = _settings.RecordingIndicatorEnabled;
        PauseMediaWhileDictating = _settings.PauseMediaWhileDictating;
        AutoAddToDictionary = _settings.AutoAddToDictionary;

        HoldHotkeyDisplay = _settings.HoldHotkey;
        ToggleHotkeyDisplay = _settings.ToggleHotkey;
        PasteLastHotkeyDisplay = _settings.PasteLastHotkey;
        CancelHotkeyDisplay = _settings.CancelHotkey;

        // Audio device
        var savedDeviceId = _settings.AudioDeviceId;
        SelectedAudioDevice = savedDeviceId is null
            ? AudioDevices.FirstOrDefault()
            : AudioDevices.FirstOrDefault(d => d.Id == savedDeviceId)
              ?? AudioDevices.FirstOrDefault();

        // Language
        var savedLang = _settings.TranscriptionLanguage;
        SelectedLanguage = savedLang is not null && Languages.Contains(savedLang)
            ? savedLang
            : "English";
    }
}

/// <summary>
/// Display wrapper for audio devices in the ComboBox.
/// </summary>
public sealed record AudioDeviceDisplay(string? Id, string DisplayName, bool IsDefault)
{
    public override string ToString() => DisplayName;
}
```

**Verify:**
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
```

---

## Task 4: Build SettingsPage XAML

- [ ] **Modify** `VoxScript/Views/SettingsPage.xaml`

Replace the entire file contents:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="VoxScript.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{StaticResource BrandBackgroundBrush}"
    KeyDown="Page_KeyDown"
    KeyUp="Page_KeyUp">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel HorizontalAlignment="Center"
                    MaxWidth="700" Padding="40" Spacing="24">

            <!-- Page Title -->
            <TextBlock Text="Settings"
                       FontFamily="Georgia" FontSize="36"
                       Foreground="{StaticResource BrandForegroundBrush}" />

            <!-- ═══ Card 1: Keybinds ═══ -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="16" Padding="24"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="16">
                    <!-- Card header -->
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE92E;" FontSize="18"
                                  Foreground="{StaticResource BrandPrimaryBrush}" />
                        <TextBlock Text="KEYBINDS" FontSize="11" FontWeight="Medium"
                                   CharacterSpacing="120"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Hold to talk -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Hold to talk"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Hold keys to record, release to stop"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <Button Grid.Column="1"
                                x:Name="HoldHotkeyButton"
                                Content="{x:Bind ViewModel.HoldHotkeyDisplay, Mode=OneWay}"
                                Click="HoldHotkeyButton_Click"
                                Background="{StaticResource BrandBackgroundBrush}"
                                Foreground="{StaticResource BrandForegroundBrush}"
                                CornerRadius="8" Padding="12,6"
                                BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                                BorderThickness="1"
                                MinWidth="120" HorizontalContentAlignment="Center" />
                    </Grid>

                    <!-- Toggle talk -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Toggle talk"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Press to start, press again to stop"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <Button Grid.Column="1"
                                x:Name="ToggleHotkeyButton"
                                Content="{x:Bind ViewModel.ToggleHotkeyDisplay, Mode=OneWay}"
                                Click="ToggleHotkeyButton_Click"
                                Background="{StaticResource BrandBackgroundBrush}"
                                Foreground="{StaticResource BrandForegroundBrush}"
                                CornerRadius="8" Padding="12,6"
                                BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                                BorderThickness="1"
                                MinWidth="120" HorizontalContentAlignment="Center" />
                    </Grid>

                    <!-- Paste last transcript -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Paste last transcript"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Paste your most recent dictation"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <Button Grid.Column="1"
                                x:Name="PasteLastHotkeyButton"
                                Content="{x:Bind ViewModel.PasteLastHotkeyDisplay, Mode=OneWay}"
                                Click="PasteLastHotkeyButton_Click"
                                Background="{StaticResource BrandBackgroundBrush}"
                                Foreground="{StaticResource BrandForegroundBrush}"
                                CornerRadius="8" Padding="12,6"
                                BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                                BorderThickness="1"
                                MinWidth="120" HorizontalContentAlignment="Center" />
                    </Grid>

                    <!-- Cancel recording -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Cancel recording"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Stop without transcribing"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <Button Grid.Column="1"
                                x:Name="CancelHotkeyButton"
                                Content="{x:Bind ViewModel.CancelHotkeyDisplay, Mode=OneWay}"
                                Click="CancelHotkeyButton_Click"
                                Background="{StaticResource BrandBackgroundBrush}"
                                Foreground="{StaticResource BrandForegroundBrush}"
                                CornerRadius="8" Padding="12,6"
                                BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                                BorderThickness="1"
                                MinWidth="120" HorizontalContentAlignment="Center" />
                    </Grid>

                    <!-- Keybind recording hint bar -->
                    <TextBlock x:Name="KeybindHint"
                               Text="Press desired key combination  ·  Esc to cancel  ·  Backspace to clear"
                               FontSize="12"
                               Foreground="{StaticResource BrandMutedBrush}"
                               HorizontalAlignment="Center"
                               Visibility="{x:Bind ViewModel.IsRecordingKeybind, Mode=OneWay}" />
                </StackPanel>
            </Border>

            <!-- ═══ Card 2: Input ═══ -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="16" Padding="24"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="16">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE720;" FontSize="18"
                                  Foreground="{StaticResource BrandPrimaryBrush}" />
                        <TextBlock Text="INPUT" FontSize="11" FontWeight="Medium"
                                   CharacterSpacing="120"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Microphone -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Microphone"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Audio input device for recording"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ComboBox Grid.Column="1"
                                  ItemsSource="{x:Bind ViewModel.AudioDevices}"
                                  SelectedItem="{x:Bind ViewModel.SelectedAudioDevice, Mode=TwoWay}"
                                  MinWidth="220"
                                  VerticalAlignment="Center" />
                    </Grid>

                    <!-- Language -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Language"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Transcription language"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ComboBox Grid.Column="1"
                                  ItemsSource="{x:Bind ViewModel.Languages}"
                                  SelectedItem="{x:Bind ViewModel.SelectedLanguage, Mode=TwoWay}"
                                  MinWidth="180"
                                  VerticalAlignment="Center" />
                    </Grid>
                </StackPanel>
            </Border>

            <!-- ═══ Card 3: App ═══ -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="16" Padding="24"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="16">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE713;" FontSize="18"
                                  Foreground="{StaticResource BrandPrimaryBrush}" />
                        <TextBlock Text="APP" FontSize="11" FontWeight="Medium"
                                   CharacterSpacing="120"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Launch at login -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Launch at login"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Start VoxScript when you sign in"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.LaunchAtLogin, Mode=TwoWay}"
                                      VerticalAlignment="Center" />
                    </Grid>

                    <!-- Recording indicator (disabled) -->
                    <Grid Opacity="0.5">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Recording indicator"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Show floating bar when dictating"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.RecordingIndicatorEnabled, Mode=TwoWay}"
                                      IsEnabled="False"
                                      VerticalAlignment="Center" />
                    </Grid>

                    <!-- Minimize to tray -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Minimize to tray"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Keep running in system tray when closed"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.MinimizeToTray, Mode=TwoWay}"
                                      VerticalAlignment="Center" />
                    </Grid>
                </StackPanel>
            </Border>

            <!-- ═══ Card 4: Sound ═══ -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="16" Padding="24"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="16">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE767;" FontSize="18"
                                  Foreground="{StaticResource BrandPrimaryBrush}" />
                        <TextBlock Text="SOUND" FontSize="11" FontWeight="Medium"
                                   CharacterSpacing="120"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Dictation sounds -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Dictation sounds"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Play audio cues when recording starts and stops"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.SoundEffectsEnabled, Mode=TwoWay}"
                                      VerticalAlignment="Center" />
                    </Grid>

                    <!-- Pause media (disabled) -->
                    <Grid Opacity="0.5">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Pause media while dictating"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Mute other audio during recording"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.PauseMediaWhileDictating, Mode=TwoWay}"
                                      IsEnabled="False"
                                      VerticalAlignment="Center" />
                    </Grid>
                </StackPanel>
            </Border>

            <!-- ═══ Card 5: Extras ═══ -->
            <Border Background="{StaticResource BrandCardBrush}"
                    CornerRadius="16" Padding="24"
                    BorderBrush="{StaticResource BrandPrimaryLightBrush}"
                    BorderThickness="1">
                <StackPanel Spacing="16">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE734;" FontSize="18"
                                  Foreground="{StaticResource BrandPrimaryBrush}" />
                        <TextBlock Text="EXTRAS" FontSize="11" FontWeight="Medium"
                                   CharacterSpacing="120"
                                   Foreground="{StaticResource BrandMutedBrush}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Auto-add to dictionary (disabled) -->
                    <Grid Opacity="0.5">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Auto-add to dictionary"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Learn frequently used words automatically"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.AutoAddToDictionary, Mode=TwoWay}"
                                      IsEnabled="False"
                                      VerticalAlignment="Center" />
                    </Grid>

                    <!-- Smart formatting -->
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="Smart formatting"
                                       FontSize="14" Foreground="{StaticResource BrandForegroundBrush}" />
                            <TextBlock Text="Auto-format punctuation and capitalization"
                                       FontSize="12" Foreground="{StaticResource BrandMutedBrush}" />
                        </StackPanel>
                        <ToggleSwitch Grid.Column="1"
                                      IsOn="{x:Bind ViewModel.SmartFormattingEnabled, Mode=TwoWay}"
                                      VerticalAlignment="Center" />
                    </Grid>
                </StackPanel>
            </Border>

            <!-- Bottom spacing -->
            <Border Height="20" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

**Verify:**
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
```

---

## Task 5: Build SettingsPage code-behind

- [ ] **Modify** `VoxScript/Views/SettingsPage.xaml.cs`

Replace the entire file contents:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoxScript.ViewModels;

namespace VoxScript.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        this.InitializeComponent();
    }

    // ── Keybind button click handlers ──────────────────────────

    private void HoldHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("HoldHotkey", (Button)sender);

    private void ToggleHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("ToggleHotkey", (Button)sender);

    private void PasteLastHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("PasteLastHotkey", (Button)sender);

    private void CancelHotkeyButton_Click(object sender, RoutedEventArgs e)
        => StartKeybindRecording("CancelHotkey", (Button)sender);

    private void StartKeybindRecording(string slot, Button button)
    {
        ViewModel.StartRecordingKeybind(slot);
        // Ensure the page has focus so it receives key events
        this.Focus(FocusState.Programmatic);
    }

    // ── Key event forwarding to ViewModel ──────────────────────

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.HandleKeyDown((int)e.Key))
        {
            e.Handled = true;
        }
    }

    private void Page_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.HandleKeyUp((int)e.Key))
        {
            e.Handled = true;
        }
    }
}
```

**Verify:**
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
```

---

## Task 6: Verify navigation wiring (already done)

Navigation is already wired. The existing `MainWindow.xaml` has a `"Settings"` `NavigationViewItem` in `FooterMenuItems`, and `MainWindow.xaml.cs` already maps `"Settings" => typeof(SettingsPage)`. No changes needed here.

- [ ] **Verify** `VoxScript/MainWindow.xaml` has `<NavigationViewItem Content="Settings" Tag="Settings" Icon="Setting" />` in FooterMenuItems.
- [ ] **Verify** `VoxScript/MainWindow.xaml.cs` has `"Settings" => typeof(SettingsPage)` in the switch expression.

**Verify:**
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
```

---

## Task 7: Full build + test pass

- [ ] Run full build:
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build VoxScript.slnx
```

- [ ] Run all tests:
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" test VoxScript.Tests
```

- [ ] Manual smoke test: launch the app, navigate to Settings from the sidebar, verify all 5 cards render, toggle switches work, keybind recording activates on click and captures combos.

---

## File Summary

| Action | File |
|--------|------|
| Modify | `VoxScript.Core/Settings/AppSettings.cs` |
| Modify | `VoxScript.Tests/Settings/AppSettingsTests.cs` |
| Create | `VoxScript/Helpers/HotkeySerializer.cs` |
| Create | `VoxScript.Tests/Settings/HotkeySerializerTests.cs` |
| Modify | `VoxScript.Tests/VoxScript.Tests.csproj` (add project ref) |
| Create | `VoxScript/ViewModels/SettingsViewModel.cs` |
| Modify | `VoxScript/Views/SettingsPage.xaml` |
| Modify | `VoxScript/Views/SettingsPage.xaml.cs` |

## Notes

- **Bool-to-Visibility binding:** The `KeybindHint` TextBlock uses `{x:Bind ViewModel.IsRecordingKeybind, Mode=OneWay}`. WinUI `x:Bind` supports implicit `bool` -> `Visibility` conversion. If this doesn't work at build time, add a `BoolToVisibilityConverter` resource.
- **Keybind button styling during recording:** The spec calls for the active badge to turn BrandPrimary with white text, and other rows to dim. This can be handled via VisualStateManager or converter bindings tied to `RecordingSlot`. Keeping it out of scope for the first pass to hit a buildable state, then add as a follow-up polish within Task 5 code-behind if time permits.
- **VirtualKey casting:** WinUI `KeyRoutedEventArgs.Key` is a `Windows.System.VirtualKey` enum. Casting to `int` gives the VK code, which matches the constants in `HotkeySerializer`. Verify at runtime that modifier keys (Ctrl, Shift, Alt, Win) arrive correctly through Page-level KeyDown -- Win key may be intercepted by the OS.
- **Test project referencing VoxScript (WinUI app):** Adding a project reference from the test project to a WinUI `WinExe` project can cause issues. If it fails, move `HotkeySerializer` to `VoxScript.Native` (which already has `HotkeyCombo` and `ModifierKeys`) and test from there instead. The test project already references `VoxScript.Native`.
