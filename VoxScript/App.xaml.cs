// VoxScript/App.xaml.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using VoxScript.Core.AI;
using VoxScript.Core.Persistence;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using ITranscriptionModel = VoxScript.Core.Transcription.Models.ITranscriptionModel;
using VoxScript.Core.Audio;
using VoxScript.Infrastructure;
using VoxScript.Native.Platform;
using VoxScript.Native.Whisper;
using VoxScript.Shell;
using VoxScript.ViewModels;

namespace VoxScript;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SystemTrayManager? _trayManager;
    private GlobalHotkeyService? _hotkey;
    private RecordingIndicatorWindow? _indicatorWindow;
    private RecordingIndicatorViewModel? _indicatorViewModel;
    private bool _onboardingActive; // when true, App's hotkey handlers stand down so the wizard's handlers own the engine

    public static Window? MainWindow { get; private set; }

    public App()
    {
        this.InitializeComponent();

        this.UnhandledException += (_, e) =>
        {
            Serilog.Log.Fatal(e.Exception, "WinUI unhandled exception");
            e.Handled = true; // prevent crash on non-fatal exceptions
        };
    }

    /// <summary>
    /// Terminates the process unconditionally. Use this for any user-initiated
    /// "quit the app" gesture (tray Exit, X-button when MinimizeToTray is off).
    ///
    /// Application.Current.Exit() is only a graceful shutdown signal — it can
    /// stall indefinitely when non-background threads (Win32 keyboard hook,
    /// WASAPI capture, native whisper.dll, ONNX Runtime) are still alive,
    /// leaving the process visible as a "Background process" in Task Manager.
    /// Environment.Exit(0) bypasses that — Windows reclaims all handles and
    /// threads when the process dies.
    /// </summary>
    public static void ExitApp()
    {
        try
        {
            Serilog.Log.Information("Application exit requested");
            Serilog.Log.CloseAndFlush();
        }
        catch { /* we're exiting; swallow */ }

        Environment.Exit(0);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLogger.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Serilog.Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");

        var services = AppBootstrapper.Build();
        ServiceLocator.Initialize(services);

        try
        {
            await AppBootstrapper.InitializeAsync(services);
            Serilog.Log.Information("Database migrated");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Database migration failed");
        }

        // Seed Power Mode configs and load into manager
        await SeedAndLoadPowerModesAsync(services);

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _trayManager = new SystemTrayManager(
            ServiceLocator.Get<VoxScriptEngine>(), _mainWindow);
        _trayManager.Initialize();

        // Recording indicator overlay
        var indicatorEngine = ServiceLocator.Get<VoxScriptEngine>();
        var indicatorSettings = ServiceLocator.Get<AppSettings>();
        _indicatorViewModel = new RecordingIndicatorViewModel(indicatorEngine, indicatorSettings);
        _indicatorWindow = new RecordingIndicatorWindow();
        _indicatorWindow.Initialize(_indicatorViewModel);
        _indicatorViewModel.ApplyInitialVisibility();

        // Register global hotkeys
        // Default: Ctrl+Win+Space to toggle, Ctrl+Win held for push-to-talk
        _hotkey = ServiceLocator.Get<GlobalHotkeyService>();
        var engine = ServiceLocator.Get<VoxScriptEngine>();

        _hotkey.RecordingToggleRequested += (_, _) =>
        {
            if (_onboardingActive) return; // wizard owns the engine during onboarding
            _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                var model = ResolveModel();
                await engine.ToggleRecordAsync(model);
                if (engine.State == VoxScript.Core.Transcription.Core.RecordingState.Recording)
                    engine.IsToggleMode = true;
            });
        };
        _hotkey.RecordingStartRequested += (_, _) =>
        {
            if (_onboardingActive) return;
            _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                if (engine.State == VoxScript.Core.Transcription.Core.RecordingState.Idle)
                {
                    var model = ResolveModel();
                    await engine.StartRecordingAsync(model);
                    engine.IsToggleMode = false; // hold mode

                    // Poll for hold→toggle conversion (Space key during hold)
                    _ = Task.Run(async () =>
                    {
                        while (engine.State == VoxScript.Core.Transcription.Core.RecordingState.Recording)
                        {
                            await Task.Delay(100);
                            if (_hotkey!.IsToggleMode && !engine.IsToggleMode)
                            {
                                _mainWindow!.DispatcherQueue.TryEnqueue(() =>
                                    engine.IsToggleMode = true);
                            }
                        }
                    });
                }
            });
        };
        _hotkey.RecordingStopRequested += (_, _) =>
        {
            if (_onboardingActive) return;
            _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                // StopAndTranscribeAsync handles its own state guards, including
                // deferred stop if the audio pipeline is still starting up.
                await engine.StopAndTranscribeAsync();
            });
        };
        _hotkey.RecordingCancelRequested += (_, _) =>
        {
            if (_onboardingActive) return;
            _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                // CancelRecordingAsync handles startup-in-progress internally.
                await engine.CancelRecordingAsync();
            });
        };
        var soundService = ServiceLocator.Get<ISoundEffectsService>();
        _hotkey.ToggleLockActivated += () =>
        {
            if (_onboardingActive) return; // wizard owns the session + sound during onboarding
            soundService.PlayToggle();
        };

        var paste = ServiceLocator.Get<VoxScript.Core.Platform.IPasteService>();
        _hotkey.PasteLastRequested += () =>
        {
            var text = engine.LastTranscription;
            if (!string.IsNullOrEmpty(text))
            {
                _mainWindow!.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await paste.PasteAtCursorAsync(text, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Paste-last failed");
                    }
                });
            }
        };
        // Apply saved hotkey bindings from settings
        var settings = ServiceLocator.Get<AppSettings>();
        var holdCombo = VoxScript.Helpers.HotkeySerializer.Parse(settings.HoldHotkey);
        var toggleCombo = VoxScript.Helpers.HotkeySerializer.Parse(settings.ToggleHotkey);
        var cancelCombo = VoxScript.Helpers.HotkeySerializer.Parse(settings.CancelHotkey);
        var pasteLastCombo = VoxScript.Helpers.HotkeySerializer.Parse(settings.PasteLastHotkey);
        if (holdCombo is not null) _hotkey.SetHoldHotkey(holdCombo.Modifiers, holdCombo.TriggerKey);
        if (toggleCombo is not null) _hotkey.SetToggleHotkey(toggleCombo.Modifiers, toggleCombo.TriggerKey);
        if (cancelCombo is not null) _hotkey.SetCancelHotkey(cancelCombo.Modifiers, cancelCombo.TriggerKey);
        if (pasteLastCombo is not null) _hotkey.SetPasteLastHotkey(pasteLastCombo.Modifiers, pasteLastCombo.TriggerKey);

        _hotkey.Register();
        Serilog.Log.Information("Global hotkeys registered: {Toggle} (toggle), {Hold} (hold), {Cancel} (cancel), {PasteLast} (paste-last)",
            settings.ToggleHotkey, settings.HoldHotkey, settings.CancelHotkey, settings.PasteLastHotkey);

        _mainWindow.Activate();

        // Resolve onboarding state and either show the wizard or proceed to
        // normal startup (including model download).
        var modelManager = ServiceLocator.Get<WhisperModelManager>();
        bool shouldShowOnboarding = ResolveOnboardingState(settings, modelManager);

        if (shouldShowOnboarding)
        {
            var onboardingVm = new VoxScript.Onboarding.OnboardingViewModel(settings);
            var micVm = new VoxScript.Onboarding.Steps.MicStepViewModel(
                ServiceLocator.Get<IAudioCaptureService>(), settings, onboardingVm);
            var modelVm = new VoxScript.Onboarding.Steps.ModelStepViewModel(
                ServiceLocator.Get<IWhisperModelManager>(),
                ServiceLocator.Get<ILocalTranscriptionBackend>(),
                settings, onboardingVm);
            var tryItVm = new VoxScript.Onboarding.Steps.TryItStepViewModel(
                ServiceLocator.Get<IGlobalHotkeyEvents>(),
                ServiceLocator.Get<IWizardEngine>(),
                settings, onboardingVm);

            var onboardingView = new VoxScript.Onboarding.OnboardingView(onboardingVm, micVm, modelVm, tryItVm);

            onboardingVm.WizardCompleted += () =>
            {
                _mainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    _onboardingActive = false; // release hotkey handlers back to the main app
                    tryItVm.Dispose();
                    await micVm.StopMonitoringAsync(); // in case finish was pressed from MicPick
                    _mainWindow.ShowShell();
                    // Ensure VAD is present even if the wizard skipped it
                    await EnsureVadModelAsync(services, modelManager);
                    if (settings.StructuralFormattingEnabled)
                        _ = ServiceLocator.Get<IStructuralFormattingService>().WarmupAsync();
                });
            };

            _onboardingActive = true;
            _mainWindow.ShowOnboarding(onboardingView);
        }
        else
        {
            // First-run model download (after window is visible so user sees progress)
            await EnsureDefaultModelAsync(services);

            if (settings.StructuralFormattingEnabled)
                _ = ServiceLocator.Get<IStructuralFormattingService>().WarmupAsync();
        }
    }

    private static bool ResolveOnboardingState(AppSettings settings, WhisperModelManager modelManager)
    {
        var completed = settings.OnboardingCompleted;
        if (completed.HasValue)
            return !completed.Value;

        // Key absent — one-time migration
        var hasModels = modelManager.ListDownloaded().Count > 0;
        if (hasModels)
        {
            settings.OnboardingCompleted = true;
            Serilog.Log.Information(
                "Onboarding: existing install detected ({Count} models on disk), skipping wizard",
                modelManager.ListDownloaded().Count);
            return false;
        }

        settings.OnboardingCompleted = false;
        Serilog.Log.Information("Onboarding: no models found, starting wizard");
        return true;
    }

    private static ITranscriptionModel ResolveModel()
    {
        var settings = ServiceLocator.Get<AppSettings>();
        var modelName = settings.SelectedModelName;

        // Check predefined models first
        var predefined = modelName is not null
            ? PredefinedModels.All.FirstOrDefault(m => m.Name == modelName)
            : null;
        if (predefined is not null) return predefined;

        // Check if it's an imported ONNX model (Parakeet)
        if (modelName is not null)
        {
            var onnxPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoxScript", "Models", "whisper", $"{modelName}.onnx");
            if (File.Exists(onnxPath))
                return new TranscriptionModel(
                    ModelProvider.Parakeet, modelName, modelName,
                    false, true, null, null);
        }

        return PredefinedModels.Default;
    }

    /// <summary>
    /// On first run, automatically download ggml-tiny.en if no models are present.
    /// Loads the model into the backend after download.
    /// </summary>
    private static async Task EnsureDefaultModelAsync(IServiceProvider services)
    {
        try
        {
            var modelManager = services.GetRequiredService<WhisperModelManager>();
            if (modelManager.ListDownloaded().Count > 0)
            {
                // At least one model exists -- load the configured or default one
                await LoadConfiguredModelAsync(services, modelManager);
                await EnsureVadModelAsync(services, modelManager);
                return;
            }

            var defaultModel = PredefinedModels.Default;
            Serilog.Log.Information("First run: downloading {Model}...", defaultModel.Name);

            var progress = new Progress<double>(pct =>
                Serilog.Log.Information("Download progress: {Pct:P0}", pct));

            await modelManager.DownloadAsync(defaultModel.Name, progress, CancellationToken.None);
            Serilog.Log.Information("Model {Model} downloaded successfully", defaultModel.Name);

            await LoadConfiguredModelAsync(services, modelManager);

            // Also download and load Silero VAD model
            await EnsureVadModelAsync(services, modelManager);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to ensure default model is available");
        }
    }

    private static async Task EnsureVadModelAsync(IServiceProvider services, WhisperModelManager modelManager)
    {
        try
        {
            if (!modelManager.IsVadDownloaded)
            {
                Serilog.Log.Information("Downloading Silero VAD model...");
                await modelManager.DownloadVadAsync(null, CancellationToken.None);
                Serilog.Log.Information("Silero VAD model downloaded");
            }

            var backend = services.GetRequiredService<WhisperBackend>();
            if (!backend.IsVadLoaded)
            {
                backend.LoadVadModel(modelManager.VadModelPath);
                Serilog.Log.Information("Silero VAD model loaded");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load Silero VAD — transcription will work without VAD");
        }
    }

    private static async Task LoadConfiguredModelAsync(
        IServiceProvider services, WhisperModelManager modelManager)
    {
        var settings = services.GetRequiredService<AppSettings>();
        var modelName = settings.SelectedModelName;

        // Check if the configured model is a Parakeet ONNX model
        var onnxDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxScript", "Models", "whisper");
        var onnxPath = Path.Combine(onnxDir, $"{modelName}.onnx");
        if (modelName is not null && File.Exists(onnxPath))
        {
            var parakeetBackend = services.GetRequiredService<VoxScript.Native.Parakeet.ParakeetBackend>();
            if (!parakeetBackend.IsModelLoaded)
            {
                Serilog.Log.Information("Loading Parakeet model: {Model}", modelName);
                await parakeetBackend.LoadModelAsync(onnxPath, CancellationToken.None);
            }
            return;
        }

        // Whisper model
        var downloaded = modelManager.ListDownloaded();
        var toLoad = modelName is not null && downloaded.Contains(modelName)
            ? modelName
            : downloaded.FirstOrDefault();

        if (toLoad is null) return;

        var modelPath = modelManager.GetModelPath(toLoad);
        var backend = services.GetRequiredService<ILocalTranscriptionBackend>();
        if (!backend.IsModelLoaded)
        {
            Serilog.Log.Information("Loading whisper model: {Model}", toLoad);
            await backend.LoadModelAsync(modelPath, CancellationToken.None);
        }
    }

    private static async Task SeedAndLoadPowerModesAsync(IServiceProvider services)
    {
        try
        {
            var repo = services.GetRequiredService<IPowerModeRepository>();
            var manager = services.GetRequiredService<PowerModeManager>();

            if (!await repo.AnyAsync(CancellationToken.None))
            {
                Serilog.Log.Information("Seeding built-in Power Mode configs");
                PowerModeConfigRecord[] seeds =
                [
                    new() { Name = "Personal Messages", ProcessNameFilter = "WhatsApp,Messenger,Discord,Telegram,Signal",
                            Preset = (int)EnhancementPreset.Casual, Priority = 10, IsBuiltIn = true, IsEnabled = true },
                    new() { Name = "Work Messages", ProcessNameFilter = "Slack,Teams",
                            Preset = (int)EnhancementPreset.SemiCasual, Priority = 10, IsBuiltIn = true, IsEnabled = true },
                    new() { Name = "Email", ProcessNameFilter = "Outlook,Thunderbird", UrlPatternFilter = @"mail\.google\.com",
                            Preset = (int)EnhancementPreset.Formal, Priority = 10, IsBuiltIn = true, IsEnabled = true },
                ];
                foreach (var seed in seeds)
                    await repo.AddAsync(seed, CancellationToken.None);
            }

            // Load all configs into the in-memory manager
            var records = await repo.GetAllAsync(CancellationToken.None);
            manager.LoadAll(records.Select(PowerModeMapper.ToConfig));
            Serilog.Log.Information("Loaded {Count} Power Mode configs", records.Count);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to seed/load Power Mode configs");
        }
    }
}
