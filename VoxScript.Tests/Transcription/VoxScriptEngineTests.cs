using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Audio;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Platform;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Processing;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class VoxScriptEngineTests
{
    [Fact]
    public void LastTranscription_is_null_by_default()
    {
        var engine = CreateEngine();
        engine.LastTranscription.Should().BeNull();
    }

    private static VoxScriptEngine CreateEngine()
    {
        var audio = Substitute.For<IAudioCaptureService>();
        var registry = new TranscriptionServiceRegistry([], []);
        var wordReplacement = new WordReplacementService(
            Substitute.For<IWordReplacementRepository>(),
            Substitute.For<IVocabularyRepository>());
        var powerModeManager = new VoxScript.Core.PowerMode.PowerModeManager();
        var powerModeSession = new VoxScript.Core.PowerMode.PowerModeSessionManager(
            powerModeManager,
            Substitute.For<VoxScript.Core.PowerMode.IActiveWindowService>());
        var settings = new AppSettings(new InMemorySettingsStore());
        var pipeline = new TranscriptionPipeline(
            new TranscriptionOutputFilter(),
            new SmartTextFormatter(),
            wordReplacement,
            Substitute.For<VoxScript.Core.AI.IAIEnhancementService>(),
            Substitute.For<VoxScript.Core.History.ITranscriptionRepository>(),
            powerModeSession,
            Substitute.For<IAutoVocabularyService>(),
            settings,
            Substitute.For<VoxScript.Core.AI.IStructuralFormattingService>());
        var paste = Substitute.For<IPasteService>();
        var sounds = Substitute.For<ISoundEffectsService>();
        var media = Substitute.For<IMediaControlService>();
        return new VoxScriptEngine(audio, registry, pipeline, settings, paste, sounds, media);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }
}
