using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.AI;
using VoxScript.Core.Audio;
using VoxScript.Core.DataPort;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Models;
using VoxScript.Helpers;
using VoxScript.Infrastructure;
using VoxScript.Native.Platform;

namespace VoxScript.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IAudioCaptureService _audioService;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly ApiKeyManager _keyManager;

    public SettingsViewModel()
    {
        _settings = ServiceLocator.Get<AppSettings>();
        _audioService = ServiceLocator.Get<IAudioCaptureService>();
        _hotkeyService = ServiceLocator.Get<GlobalHotkeyService>();
        _keyManager = ServiceLocator.Get<ApiKeyManager>();

        LoadAudioDevices();
        LoadSettings();
    }

    // ── Current Model ─────────────────────────────────────────

    [ObservableProperty]
    public partial string CurrentModelDisplay { get; set; } = "";

    public void RefreshCurrentModel()
    {
        var name = _settings.SelectedModelName;
        var model = name is not null
            ? PredefinedModels.All.FirstOrDefault(m => m.Name == name)
            : null;
        CurrentModelDisplay = model?.DisplayName ?? name ?? PredefinedModels.Default.DisplayName;
    }

    // ── AI Enhancement ──────────────────────────────────────────

    [ObservableProperty]
    public partial bool AiEnhancementEnabled { get; set; }
    partial void OnAiEnhancementEnabledChanged(bool value) => _settings.AiEnhancementEnabled = value;

    public IReadOnlyList<string> AiProviders { get; } = ["Local (Ollama)", "OpenAI", "Anthropic"];

    [ObservableProperty]
    public partial int SelectedAiProviderIndex { get; set; }

    partial void OnSelectedAiProviderIndexChanged(int value)
    {
        var provider = value switch { 1 => AiProvider.OpenAI, 2 => AiProvider.Anthropic, _ => AiProvider.Local };
        _settings.AiProvider = provider;

        // Set default model when switching providers
        _settings.AiModelName = provider switch
        {
            AiProvider.OpenAI => "gpt-4o-mini",
            AiProvider.Anthropic => "claude-sonnet-4-20250514",
            _ => "llama3.2",
        };
        AiModelName = _settings.AiModelName;
        OnPropertyChanged(nameof(IsLocalProvider));
        OnPropertyChanged(nameof(IsCloudProvider));
        UpdateAiStatus();
    }

    public bool IsLocalProvider => SelectedAiProviderIndex == 0;
    public bool IsCloudProvider => SelectedAiProviderIndex > 0;

    [ObservableProperty]
    public partial string AiModelName { get; set; } = "";
    partial void OnAiModelNameChanged(string value) => _settings.AiModelName = value;

    [ObservableProperty]
    public partial string OllamaEndpoint { get; set; } = "";
    partial void OnOllamaEndpointChanged(string value) => _settings.OllamaEndpoint = value;

    [ObservableProperty]
    public partial string ApiKeyDisplay { get; set; } = "";

    [ObservableProperty]
    public partial string AiStatusText { get; set; } = "";

    public void SaveApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (_settings.AiProvider == AiProvider.OpenAI)
            _keyManager.SetOpenAiKey(key);
        else if (_settings.AiProvider == AiProvider.Anthropic)
            _keyManager.SetAnthropicKey(key);
        UpdateApiKeyDisplay();
        UpdateAiStatus();
    }

    public void ClearApiKey()
    {
        if (_settings.AiProvider == AiProvider.OpenAI)
            _keyManager.SetOpenAiKey("");
        else if (_settings.AiProvider == AiProvider.Anthropic)
            _keyManager.SetAnthropicKey("");
        UpdateApiKeyDisplay();
        UpdateAiStatus();
    }

    private void UpdateApiKeyDisplay()
    {
        var key = _settings.AiProvider switch
        {
            AiProvider.OpenAI => _keyManager.GetOpenAiKey(),
            AiProvider.Anthropic => _keyManager.GetAnthropicKey(),
            _ => null,
        };
        ApiKeyDisplay = key is { Length: > 8 } ? $"{key[..4]}...{key[^4..]}" : key is { Length: > 0 } ? "****" : "";
    }

    private void UpdateAiStatus()
    {
        if (!AiEnhancementEnabled)
        {
            AiStatusText = "";
            return;
        }
        AiStatusText = _settings.AiProvider switch
        {
            AiProvider.OpenAI => _keyManager.GetOpenAiKey() is { Length: > 0 } ? "Configured" : "Not configured — enter API key",
            AiProvider.Anthropic => _keyManager.GetAnthropicKey() is { Length: > 0 } ? "Configured" : "Not configured — enter API key",
            AiProvider.Local => "Using Ollama at " + _settings.OllamaEndpoint,
            _ => "",
        };
    }

    // ── LLM Structural Formatting ──────────────────────────────────

    [ObservableProperty]
    public partial bool StructuralFormattingEnabled { get; set; }
    partial void OnStructuralFormattingEnabledChanged(bool value)
    {
        _settings.StructuralFormattingEnabled = value;
        // Off → on: warm the model so the user's first dictation isn't slow.
        // No-op for cloud providers and when not configured.
        if (value)
            _ = ServiceLocator.Get<IStructuralFormattingService>().WarmupAsync();
    }

    public IReadOnlyList<string> StructuralAiProviders { get; } = ["Local (Ollama)", "OpenAI", "Anthropic"];

    [ObservableProperty]
    public partial int SelectedStructuralAiProviderIndex { get; set; }

    partial void OnSelectedStructuralAiProviderIndexChanged(int value)
    {
        var newProvider = value switch
        {
            1 => AiProvider.OpenAI,
            2 => AiProvider.Anthropic,
            _ => AiProvider.Local
        };

        // Swap to provider default only if the model is still the previous default
        var previousDefault = _settings.StructuralAiProvider switch
        {
            AiProvider.OpenAI    => "gpt-4o-mini",
            AiProvider.Anthropic => "claude-haiku-4-5-20251001",
            _                    => "qwen2.5:7b",
        };
        var newDefault = newProvider switch
        {
            AiProvider.OpenAI    => "gpt-4o-mini",
            AiProvider.Anthropic => "claude-haiku-4-5-20251001",
            _                    => "qwen2.5:7b",
        };

        if (_settings.StructuralAiModel == previousDefault)
        {
            _settings.StructuralAiModel = newDefault;
            StructuralAiModel = newDefault;
        }

        _settings.StructuralAiProvider = newProvider;
        OnPropertyChanged(nameof(IsStructuralLocalProvider));
        OnPropertyChanged(nameof(IsStructuralCloudProvider));
        UpdateStructuralStatus();
    }

    public bool IsStructuralLocalProvider  => SelectedStructuralAiProviderIndex == 0;
    public bool IsStructuralCloudProvider  => SelectedStructuralAiProviderIndex > 0;

    [ObservableProperty]
    public partial string StructuralAiModel { get; set; } = "";
    partial void OnStructuralAiModelChanged(string value) => _settings.StructuralAiModel = value;

    [ObservableProperty]
    public partial string StructuralOllamaEndpoint { get; set; } = "";
    partial void OnStructuralOllamaEndpointChanged(string value) => _settings.StructuralOllamaEndpoint = value;

    [ObservableProperty]
    public partial string StructuralApiKeyDisplay { get; set; } = "";

    [ObservableProperty]
    public partial string StructuralStatusText { get; set; } = "";

    public void SaveStructuralApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (_settings.StructuralAiProvider == AiProvider.OpenAI)
            _keyManager.SetStructuralOpenAiKey(key);
        else if (_settings.StructuralAiProvider == AiProvider.Anthropic)
            _keyManager.SetStructuralAnthropicKey(key);
        UpdateStructuralApiKeyDisplay();
        UpdateStructuralStatus();
    }

    public void ClearStructuralApiKey()
    {
        if (_settings.StructuralAiProvider == AiProvider.OpenAI)
            _keyManager.SetStructuralOpenAiKey("");
        else if (_settings.StructuralAiProvider == AiProvider.Anthropic)
            _keyManager.SetStructuralAnthropicKey("");
        UpdateStructuralApiKeyDisplay();
        UpdateStructuralStatus();
    }

    /// <summary>
    /// Returns the current structural-formatting system prompt — the user's
    /// override if set, otherwise the built-in default.
    /// </summary>
    public string GetEffectiveStructuralPrompt() =>
        string.IsNullOrWhiteSpace(_settings.StructuralFormattingPromptOverride)
            ? VoxScript.Core.AI.StructuralFormattingPrompt.System
            : _settings.StructuralFormattingPromptOverride!;

    /// <summary>
    /// Save a custom system prompt. Whitespace/empty input clears the override
    /// so the built-in default is used.
    /// </summary>
    public void SaveStructuralPromptOverride(string prompt)
    {
        _settings.StructuralFormattingPromptOverride =
            string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }

    public void ResetStructuralPromptToDefault()
    {
        _settings.StructuralFormattingPromptOverride = null;
    }

    private void UpdateStructuralApiKeyDisplay()
    {
        var key = _settings.StructuralAiProvider switch
        {
            AiProvider.OpenAI    => _keyManager.GetStructuralOpenAiKey(),
            AiProvider.Anthropic => _keyManager.GetStructuralAnthropicKey(),
            _                    => null,
        };
        StructuralApiKeyDisplay = key is { Length: > 8 }
            ? $"{key[..4]}...{key[^4..]}"
            : key is { Length: > 0 } ? "****" : "";
    }

    private void UpdateStructuralStatus()
    {
        if (!StructuralFormattingEnabled)
        {
            StructuralStatusText = "";
            return;
        }
        StructuralStatusText = _settings.StructuralAiProvider switch
        {
            AiProvider.OpenAI    => _keyManager.GetStructuralOpenAiKey() is { Length: > 0 }
                ? "Configured" : "Not configured — enter API key",
            AiProvider.Anthropic => _keyManager.GetStructuralAnthropicKey() is { Length: > 0 }
                ? "Configured" : "Not configured — enter API key",
            AiProvider.Local     => "Using Ollama at " + _settings.StructuralOllamaEndpoint,
            _                    => "",
        };
    }

    // ── Audio Devices ──────────────────────────────────────────

    public ObservableCollection<AudioDeviceDisplay> AudioDevices { get; } = new();

    [ObservableProperty]
    public partial AudioDeviceDisplay? SelectedAudioDevice { get; set; }

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

    public IReadOnlyList<string> Languages { get; } = new[]
    {
        "English", "Spanish", "French", "German", "Italian", "Portuguese",
        "Dutch", "Russian", "Chinese", "Japanese", "Korean", "Arabic",
        "Hindi", "Turkish", "Polish", "Ukrainian", "Czech", "Danish",
        "Finnish", "Greek", "Hebrew", "Hungarian", "Indonesian", "Malay",
        "Norwegian", "Romanian", "Swedish", "Thai", "Vietnamese",
    };

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = "English";

    partial void OnSelectedLanguageChanged(string value)
    {
        _settings.TranscriptionLanguage = value;
    }

    // ── Toggle settings (auto-save on change) ──────────────────

    [ObservableProperty]
    public partial bool LaunchAtLogin { get; set; }
    partial void OnLaunchAtLoginChanged(bool value) => _settings.LaunchAtLogin = value;

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }
    partial void OnMinimizeToTrayChanged(bool value) => _settings.MinimizeToTray = value;

    [ObservableProperty]
    public partial bool SmartFormattingEnabled { get; set; }
    partial void OnSmartFormattingEnabledChanged(bool value) => _settings.SmartFormattingEnabled = value;

    [ObservableProperty]
    public partial bool SoundEffectsEnabled { get; set; }
    partial void OnSoundEffectsEnabledChanged(bool value) => _settings.SoundEffectsEnabled = value;

    [ObservableProperty]
    public partial int RecordingIndicatorModeIndex { get; set; }
    partial void OnRecordingIndicatorModeIndexChanged(int value)
    {
        _settings.RecordingIndicatorMode = (RecordingIndicatorMode)value;
    }

    public List<string> RecordingIndicatorModes { get; } = ["Off", "Always visible", "Only during recording"];

    [ObservableProperty]
    public partial bool PauseMediaWhileDictating { get; set; }
    partial void OnPauseMediaWhileDictatingChanged(bool value) => _settings.PauseMediaWhileDictating = value;

    [ObservableProperty]
    public partial bool AutoAddToDictionary { get; set; }
    partial void OnAutoAddToDictionaryChanged(bool value) => _settings.AutoAddToDictionary = value;

    // ── Import / Export ────────────────────────────────────────

    [ObservableProperty]
    public partial string DataPortStatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool DataPortIsError { get; set; }

    public async Task ExportDataAsync(Stream output, CancellationToken ct)
    {
        try
        {
            var service = ServiceLocator.Get<IDataPortService>();
            var result = await service.ExportAsync(output, ct);
            DataPortIsError = false;
            DataPortStatusMessage = $"Exported {result.VocabularyCount} vocabulary words, {result.CorrectionsCount} corrections, {result.ExpansionsCount} expansions.";
        }
        catch (Exception ex)
        {
            DataPortIsError = true;
            DataPortStatusMessage = $"Export failed: {ex.Message}";
        }
    }

    public async Task ImportDataAsync(Stream input, CancellationToken ct)
    {
        try
        {
            var service = ServiceLocator.Get<IDataPortService>();
            var result = await service.ImportAsync(input, ct);
            DataPortIsError = false;
            DataPortStatusMessage = $"Imported {result.VocabularyAdded} vocabulary words, {result.CorrectionsAdded} corrections, {result.ExpansionsAdded} expansions ({result.Skipped} skipped).";
        }
        catch (InvalidOperationException ex)
        {
            DataPortIsError = true;
            DataPortStatusMessage = ex.Message;
        }
    }

    // ── Keybinds ───────────────────────────────────────────────

    [ObservableProperty]
    public partial string HoldHotkeyDisplay { get; set; } = "Ctrl+Win";

    [ObservableProperty]
    public partial string ToggleHotkeyDisplay { get; set; } = "Ctrl+Win+Space";

    [ObservableProperty]
    public partial string PasteLastHotkeyDisplay { get; set; } = "Alt+Shift+Z";

    [ObservableProperty]
    public partial string CancelHotkeyDisplay { get; set; } = "Esc";

    // Recording state
    [ObservableProperty]
    public partial bool IsRecordingKeybind { get; set; }

    [ObservableProperty]
    public partial string? RecordingSlot { get; set; }

    [ObservableProperty]
    public partial string RecordingPreview { get; set; } = "";

    private string _preRecordValue = "";
    private ModifierKeys _recordingModifiers = ModifierKeys.None;
    private Microsoft.UI.Xaml.DispatcherTimer? _pollTimer;

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
        _recordingModifiers = ModifierKeys.None;

        // Clear the display to show recording state
        SetSlotDisplay(slot, "");

        _hotkeyService.Unregister();

        // Start polling for keys that WinUI can't see (e.g. Win+Space)
        _pollTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // Common trigger keys to poll for (ones WinUI may not report when Win is held)
    private static readonly int[] PollKeys = [
        0x20, // Space
        0x0D, // Enter
        0x09, // Tab
        // A-Z
        0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A,
        0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54,
        0x55, 0x56, 0x57, 0x58, 0x59, 0x5A,
        // 0-9
        0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        // F1-F12
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B,
    ];

    private void PollTimer_Tick(object? sender, object e)
    {
        if (!IsRecordingKeybind) { StopPollTimer(); return; }

        var mods = PollModifiers();

        // Update preview if modifiers changed
        if (mods != _recordingModifiers && mods != ModifierKeys.None)
        {
            _recordingModifiers = mods;
            SetSlotDisplay(RecordingSlot!, HotkeySerializer.Serialize(
                new HotkeyCombo(_recordingModifiers, null)) + "+...");
        }

        // Check for Escape
        if (IsKeyDown(0x1B))
        {
            StopPollTimer();
            CancelRecording();
            return;
        }

        // Check for Backspace
        if (IsKeyDown(0x08))
        {
            StopPollTimer();
            CommitKeybind("Not set");
            return;
        }

        // Check for trigger key press while modifiers are held
        if (mods != ModifierKeys.None)
        {
            foreach (var vk in PollKeys)
            {
                if (IsKeyDown(vk))
                {
                    StopPollTimer();
                    var combo = new HotkeyCombo(mods, vk);
                    CommitKeybind(HotkeySerializer.Serialize(combo));
                    return;
                }
            }
        }

        // Check if all modifiers released (modifier-only combo)
        if (mods == ModifierKeys.None && _recordingModifiers != ModifierKeys.None)
        {
            StopPollTimer();
            var combo = new HotkeyCombo(_recordingModifiers, null);
            CommitKeybind(HotkeySerializer.Serialize(combo));
        }
    }

    private void StopPollTimer()
    {
        if (_pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= PollTimer_Tick;
            _pollTimer = null;
        }
    }

    /// <summary>
    /// Poll physical modifier state via GetAsyncKeyState.
    /// WinUI KeyDown events don't fire for the Win key (OS intercepts it),
    /// so we poll all modifiers whenever any key event arrives.
    /// </summary>
    private static ModifierKeys PollModifiers()
    {
        var mods = ModifierKeys.None;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= ModifierKeys.Ctrl;   // VK_CONTROL
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= ModifierKeys.Shift;   // VK_SHIFT
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= ModifierKeys.Alt;     // VK_MENU
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
            (GetAsyncKeyState(0x5C) & 0x8000) != 0) mods |= ModifierKeys.Win;     // VK_LWIN/RWIN
        return mods;
    }

    public bool HandleKeyDown(int vkCode)
    {
        if (!IsRecordingKeybind) return false;

        // Escape cancels recording
        if (vkCode == 0x1B)
        {
            CancelRecording();
            return true;
        }

        // Backspace clears binding
        if (vkCode == 0x08)
        {
            CommitKeybind("Not set");
            return true;
        }

        // Poll real modifier state (catches Win key that WinUI doesn't report)
        _recordingModifiers = PollModifiers();

        if (HotkeySerializer.IsModifierKey(vkCode))
        {
            // Show current modifiers as preview
            SetSlotDisplay(RecordingSlot!, HotkeySerializer.Serialize(
                new HotkeyCombo(_recordingModifiers, null)) + "+...");
            return true;
        }

        // Non-modifier key pressed — commit with current modifiers
        var combo = new HotkeyCombo(_recordingModifiers, vkCode);
        CommitKeybind(HotkeySerializer.Serialize(combo));
        return true;
    }

    public bool HandleKeyUp(int vkCode)
    {
        if (!IsRecordingKeybind) return false;

        if (HotkeySerializer.IsModifierKey(vkCode))
        {
            // Poll actual state — only commit if ALL modifiers are now released
            var currentMods = PollModifiers();
            if (currentMods == ModifierKeys.None && _recordingModifiers != ModifierKeys.None)
            {
                // All modifiers released — commit modifier-only combo
                var combo = new HotkeyCombo(_recordingModifiers, null);
                CommitKeybind(HotkeySerializer.Serialize(combo));
            }
            return true;
        }

        return false;
    }

    private void SetSlotDisplay(string slot, string value)
    {
        switch (slot)
        {
            case "HoldHotkey": HoldHotkeyDisplay = value; break;
            case "ToggleHotkey": ToggleHotkeyDisplay = value; break;
            case "PasteLastHotkey": PasteLastHotkeyDisplay = value; break;
            case "CancelHotkey": CancelHotkeyDisplay = value; break;
        }
    }

    private void CommitKeybind(string displayValue)
    {
        SetSlotDisplay(RecordingSlot!, displayValue);

        switch (RecordingSlot)
        {
            case "HoldHotkey": _settings.HoldHotkey = displayValue; break;
            case "ToggleHotkey": _settings.ToggleHotkey = displayValue; break;
            case "PasteLastHotkey": _settings.PasteLastHotkey = displayValue; break;
            case "CancelHotkey": _settings.CancelHotkey = displayValue; break;
        }

        ExitRecordingMode();
        ApplyHotkeysToService();
    }

    private void CancelRecording()
    {
        SetSlotDisplay(RecordingSlot!, _preRecordValue);
        ExitRecordingMode();
    }

    public void ResetKeybindsToDefaults()
    {
        HoldHotkeyDisplay = "Ctrl+Win";
        ToggleHotkeyDisplay = "Ctrl+Win+Space";
        PasteLastHotkeyDisplay = "Alt+Shift+Z";
        CancelHotkeyDisplay = "Esc";

        _settings.HoldHotkey = "Ctrl+Win";
        _settings.ToggleHotkey = "Ctrl+Win+Space";
        _settings.PasteLastHotkey = "Alt+Shift+Z";
        _settings.CancelHotkey = "Esc";

        ApplyHotkeysToService();
    }

    private void ExitRecordingMode()
    {
        StopPollTimer();
        IsRecordingKeybind = false;
        RecordingSlot = null;
        RecordingPreview = "";
        _recordingModifiers = ModifierKeys.None;

        _hotkeyService.Register();
    }

    private void ApplyHotkeysToService()
    {
        var holdCombo = HotkeySerializer.Parse(_settings.HoldHotkey);
        var toggleCombo = HotkeySerializer.Parse(_settings.ToggleHotkey);
        var cancelCombo = HotkeySerializer.Parse(_settings.CancelHotkey);

        if (holdCombo is not null)
            _hotkeyService.SetHoldHotkey(holdCombo.Modifiers, holdCombo.TriggerKey);
        if (toggleCombo is not null)
            _hotkeyService.SetToggleHotkey(toggleCombo.Modifiers, toggleCombo.TriggerKey);
        if (cancelCombo is not null)
            _hotkeyService.SetCancelHotkey(cancelCombo.Modifiers, cancelCombo.TriggerKey);
    }

    // ── Load from persisted settings ───────────────────────────

    private void LoadSettings()
    {
        LaunchAtLogin = _settings.LaunchAtLogin;
        MinimizeToTray = _settings.MinimizeToTray;
        SmartFormattingEnabled = _settings.SmartFormattingEnabled;
        SoundEffectsEnabled = _settings.SoundEffectsEnabled;
        RecordingIndicatorModeIndex = (int)_settings.RecordingIndicatorMode;
        PauseMediaWhileDictating = _settings.PauseMediaWhileDictating;
        AutoAddToDictionary = _settings.AutoAddToDictionary;

        HoldHotkeyDisplay = _settings.HoldHotkey;
        ToggleHotkeyDisplay = _settings.ToggleHotkey;
        PasteLastHotkeyDisplay = _settings.PasteLastHotkey;
        CancelHotkeyDisplay = _settings.CancelHotkey;

        RefreshCurrentModel();

        var savedDeviceId = _settings.AudioDeviceId;
        SelectedAudioDevice = savedDeviceId is null
            ? AudioDevices.FirstOrDefault()
            : AudioDevices.FirstOrDefault(d => d.Id == savedDeviceId)
              ?? AudioDevices.FirstOrDefault();

        var savedLang = _settings.TranscriptionLanguage;
        SelectedLanguage = savedLang is not null && Languages.Contains(savedLang)
            ? savedLang
            : "English";

        // AI Enhancement
        AiEnhancementEnabled = _settings.AiEnhancementEnabled;
        SelectedAiProviderIndex = _settings.AiProvider switch
        {
            AiProvider.OpenAI => 1,
            AiProvider.Anthropic => 2,
            _ => 0,
        };
        AiModelName = _settings.AiModelName;
        OllamaEndpoint = _settings.OllamaEndpoint;
        UpdateApiKeyDisplay();
        UpdateAiStatus();

        // LLM Structural Formatting
        StructuralFormattingEnabled = _settings.StructuralFormattingEnabled;
        SelectedStructuralAiProviderIndex = _settings.StructuralAiProvider switch
        {
            AiProvider.OpenAI    => 1,
            AiProvider.Anthropic => 2,
            _                    => 0,
        };
        StructuralAiModel      = _settings.StructuralAiModel;
        StructuralOllamaEndpoint = _settings.StructuralOllamaEndpoint;
        UpdateStructuralApiKeyDisplay();
        UpdateStructuralStatus();
    }
}

public sealed record AudioDeviceDisplay(string? Id, string DisplayName, bool IsDefault)
{
    public override string ToString() => DisplayName;
}
