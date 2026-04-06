// VoxScript/Infrastructure/AppBootstrapper.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoxScript.Core.AI;
using VoxScript.Core.Audio;
using VoxScript.Core.Dictionary;
using VoxScript.Core.History;
using VoxScript.Core.Persistence;
using VoxScript.Core.Platform;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Notes;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Batch;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Processing;
using VoxScript.Core.Transcription.Streaming;
using VoxScript.Native.Audio;
using VoxScript.Native.Platform;
using VoxScript.Native.Storage;
using VoxScript.Native.Whisper;


namespace VoxScript.Infrastructure;

public static class AppBootstrapper
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxScript", "voxscript.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Singleton);

        // Native singletons
        services.AddSingleton<IAudioCaptureService, WasapiCaptureService>();
        services.AddSingleton<ISoundEffectsService, VoxScript.Native.Audio.SoundEffectsService>();
        services.AddSingleton<IMediaControlService, MediaControlService>();
        services.AddSingleton<ISettingsStore, LocalSettingsStore>();
        services.AddSingleton<IActiveWindowService, ActiveWindowService>();
        services.AddSingleton<IApiKeyStore, WindowsCredentialService>();
        services.AddSingleton<CursorPasterService>();
        services.AddSingleton<IPasteService>(sp => sp.GetRequiredService<CursorPasterService>());
        services.AddSingleton<GlobalHotkeyService>();

        // Whisper -- register the concrete type and both interfaces it implements
        services.AddSingleton<WhisperBackend>();
        services.AddSingleton<IWhisperBackend>(sp => sp.GetRequiredService<WhisperBackend>());
        services.AddSingleton<ILocalTranscriptionBackend>(sp => sp.GetRequiredService<WhisperBackend>());
        services.AddSingleton<WhisperModelManager>(sp =>
        {
            var modelsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoxScript", "Models", "whisper");
            return new WhisperModelManager(modelsDir, new HttpClient());
        });

        // Core services
        services.AddSingleton<AppSettings>();
        services.AddSingleton<ApiKeyManager>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<AIService>();
        services.AddSingleton<AIEnhancementOutputFilter>();
        services.AddSingleton<IAIEnhancementService, AIEnhancementService>();
        services.AddSingleton<TranscriptionOutputFilter>();
        services.AddSingleton<WhisperTextFormatter>();
        services.AddSingleton<IWordReplacementRepository, WordReplacementRepository>();
        services.AddSingleton<IVocabularyRepository, VocabularyRepository>();
        services.AddSingleton<ICorrectionRepository, CorrectionRepository>();
        services.AddSingleton<WordReplacementService>();
        services.AddSingleton<ITranscriptionRepository, TranscriptionRepository>();
        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<TranscriptionPipeline>();

        // PowerMode
        services.AddSingleton<IPowerModeRepository, PowerModeRepository>();
        services.AddSingleton<PowerModeManager>();
        services.AddSingleton<PowerModeSessionManager>();

        // Transcription services (registered as IEnumerable<ITranscriptionService>)
        services.AddSingleton<ITranscriptionService, LocalTranscriptionService>();
        services.AddSingleton<ITranscriptionService, CloudTranscriptionService>();

        // Streaming providers (registered as IEnumerable<IStreamingProvider>)
        services.AddSingleton<IStreamingProvider, DeepgramStreamingProvider>();
        services.AddSingleton<IStreamingProvider, ElevenLabsStreamingProvider>();

        services.AddSingleton<TranscriptionServiceRegistry>();
        services.AddSingleton<VoxScriptEngine>();

        return services.BuildServiceProvider();
    }
}
