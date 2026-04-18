using VoxScript.Core.AI;

namespace VoxScript.Core.Settings;

public sealed class AppSettings
{
    private readonly ISettingsStore _store;
    public AppSettings(ISettingsStore store) => _store = store;

    public string? SelectedModelName
    {
        get => _store.Get<string>(nameof(SelectedModelName));
        set => _store.Set(nameof(SelectedModelName), value);
    }

    public string? AudioDeviceId
    {
        get => _store.Get<string>(nameof(AudioDeviceId));
        set => _store.Set(nameof(AudioDeviceId), value);
    }

    public string? TranscriptionLanguage
    {
        get => _store.Get<string>(nameof(TranscriptionLanguage));
        set => _store.Set(nameof(TranscriptionLanguage), value);
    }

    public bool AiEnhancementEnabled
    {
        get => _store.Get<bool?>(nameof(AiEnhancementEnabled)) ?? false;
        set => _store.Set(nameof(AiEnhancementEnabled), value);
    }

    public AiProvider AiProvider
    {
        get => _store.Get<AiProvider?>(nameof(AiProvider)) ?? AiProvider.Local;
        set => _store.Set(nameof(AiProvider), value);
    }

    public string AiModelName
    {
        get => _store.Get<string>(nameof(AiModelName)) ?? "llama3.2";
        set => _store.Set(nameof(AiModelName), value);
    }

    public string OllamaEndpoint
    {
        get => _store.Get<string>(nameof(OllamaEndpoint)) ?? "http://localhost:11434";
        set => _store.Set(nameof(OllamaEndpoint), value);
    }

    public EnhancementPreset EnhancementPreset
    {
        get => _store.Get<EnhancementPreset?>(nameof(EnhancementPreset)) ?? EnhancementPreset.SemiCasual;
        set => _store.Set(nameof(EnhancementPreset), value);
    }

    public string EnhancementPunctuation
    {
        get => _store.Get<string>(nameof(EnhancementPunctuation)) ?? "Standard";
        set => _store.Set(nameof(EnhancementPunctuation), value);
    }

    public string EnhancementCapitalization
    {
        get => _store.Get<string>(nameof(EnhancementCapitalization)) ?? "SentenceCase";
        set => _store.Set(nameof(EnhancementCapitalization), value);
    }

    public bool EnhancementRemoveFillers
    {
        get => _store.Get<bool?>(nameof(EnhancementRemoveFillers)) ?? true;
        set => _store.Set(nameof(EnhancementRemoveFillers), value);
    }

    public string EnhancementSystemPrompt
    {
        get => _store.Get<string>(nameof(EnhancementSystemPrompt)) ?? EnhancementPrompts.SemiCasual;
        set => _store.Set(nameof(EnhancementSystemPrompt), value);
    }

    public bool AutoPasteEnabled
    {
        get => _store.Get<bool?>(nameof(AutoPasteEnabled)) ?? true;
        set => _store.Set(nameof(AutoPasteEnabled), value);
    }

    public bool SoundEffectsEnabled
    {
        get => _store.Get<bool?>(nameof(SoundEffectsEnabled)) ?? true;
        set => _store.Set(nameof(SoundEffectsEnabled), value);
    }

    public HotkeyMode PrimaryHotkeyMode
    {
        get => _store.Get<HotkeyMode?>(nameof(PrimaryHotkeyMode)) ?? HotkeyMode.Toggle;
        set => _store.Set(nameof(PrimaryHotkeyMode), value);
    }

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

    public RecordingIndicatorMode RecordingIndicatorMode
    {
        get => _store.Get<RecordingIndicatorMode?>(nameof(RecordingIndicatorMode)) ?? RecordingIndicatorMode.DuringRecording;
        set
        {
            _store.Set(nameof(RecordingIndicatorMode), value);
            RecordingIndicatorModeChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<RecordingIndicatorMode>? RecordingIndicatorModeChanged;

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

    public bool StructuralFormattingEnabled
    {
        get => _store.Get<bool?>(nameof(StructuralFormattingEnabled)) ?? false;
        set => _store.Set(nameof(StructuralFormattingEnabled), value);
    }

    public AiProvider StructuralAiProvider
    {
        get => _store.Get<AiProvider?>(nameof(StructuralAiProvider)) ?? AiProvider.Local;
        set => _store.Set(nameof(StructuralAiProvider), value);
    }

    public string StructuralAiModel
    {
        get => _store.Get<string>(nameof(StructuralAiModel)) ?? "qwen2.5:7b";
        set => _store.Set(nameof(StructuralAiModel), value);
    }

    public string StructuralOllamaEndpoint
    {
        get => _store.Get<string>(nameof(StructuralOllamaEndpoint)) ?? "http://localhost:11434";
        set => _store.Set(nameof(StructuralOllamaEndpoint), value);
    }

    /// <summary>
    /// User-defined system prompt that overrides the built-in
    /// <see cref="VoxScript.Core.AI.StructuralFormattingPrompt.System"/> when set.
    /// Null/whitespace means "use the built-in default".
    /// </summary>
    public string? StructuralFormattingPromptOverride
    {
        get => _store.Get<string>(nameof(StructuralFormattingPromptOverride));
        set => _store.Set(nameof(StructuralFormattingPromptOverride), value);
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

    /// <summary>
    /// Null = key absent (unknown / pre-migration). True = wizard completed.
    /// False = wizard not yet completed (resolved by the startup migration).
    /// </summary>
    public bool? OnboardingCompleted
    {
        get => _store.Get<bool?>(nameof(OnboardingCompleted));
        set
        {
            if (value is null)
                _store.Remove(nameof(OnboardingCompleted));
            else
                _store.Set(nameof(OnboardingCompleted), value);
        }
    }
}

public enum HotkeyMode { Toggle, PushToTalk, Hybrid }
