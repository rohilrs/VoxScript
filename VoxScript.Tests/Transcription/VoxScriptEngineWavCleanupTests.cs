using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Audio;
using VoxScript.Core.Dictionary;
using VoxScript.Core.Platform;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Core.Transcription.Processing;

namespace VoxScript.Tests.Transcription;

/// <summary>
/// Verifies that <see cref="VoxScriptEngine"/> deletes the WAV file on every
/// recording-lifecycle exit path: successful pipeline completion, pipeline
/// failure, and the short-recording early-return branch.
/// </summary>
public class VoxScriptEngineWavCleanupTests : IDisposable
{
    private readonly string _recordingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoxScript", "Recordings");

    // Paths the engine used during the test — tracked so Dispose can clean up
    // leftover files if an assertion fails before the engine deletes them.
    private readonly List<string> _touchedPaths = new();

    // Files that existed in the Recordings directory before the test ran, so
    // the short-recording test can diff before/after to detect leaked WAVs.
    private readonly HashSet<string> _preExistingFiles;

    public VoxScriptEngineWavCleanupTests()
    {
        Directory.CreateDirectory(_recordingsDir);
        _preExistingFiles = Directory.GetFiles(_recordingsDir, "rec_*.wav").ToHashSet();
    }

    // ── Test: success path ────────────────────────────────────────────────

    [Fact]
    public async Task StopAndTranscribe_deletes_wav_after_successful_transcription()
    {
        var fakeService = new CapturingTranscriptionService { Result = "hello world" };
        var engine = CreateEngine(fakeService);
        var model = CreateLocalModel();

        await engine.StartRecordingAsync(model);
        await Task.Delay(600); // Clear 0.5s MinRecordingSeconds threshold.
        await engine.StopAndTranscribeAsync();

        fakeService.CapturedPath.Should().NotBeNull(
            "engine must have reached the pipeline and invoked TranscribeAsync");
        _touchedPaths.Add(fakeService.CapturedPath!);

        File.Exists(fakeService.CapturedPath!).Should().BeFalse(
            "engine must delete the WAV file after the pipeline completes");
    }

    // ── Test: failure path ────────────────────────────────────────────────

    [Fact]
    public async Task StopAndTranscribe_deletes_wav_when_pipeline_throws()
    {
        var fakeService = new CapturingTranscriptionService
        {
            Throw = () => throw new InvalidOperationException("transcription backend down"),
        };
        var engine = CreateEngine(fakeService);
        var model = CreateLocalModel();

        await engine.StartRecordingAsync(model);
        await Task.Delay(600); // Clear short-recording gate so TranscribeAsync runs.

        // Engine catches pipeline exceptions internally (see the try/catch in
        // StopAndTranscribeAsync) — the test itself should not observe a throw.
        await engine.StopAndTranscribeAsync();

        fakeService.CapturedPath.Should().NotBeNull(
            "TranscribeAsync must have been invoked and captured the path before throwing");
        _touchedPaths.Add(fakeService.CapturedPath!);

        File.Exists(fakeService.CapturedPath!).Should().BeFalse(
            "engine must delete the WAV even when the pipeline throws");
    }

    // ── Test: short-recording path ────────────────────────────────────────

    [Fact]
    public async Task StopAndTranscribe_deletes_wav_on_short_recording()
    {
        var fakeService = new CapturingTranscriptionService { Result = "unused" };
        var engine = CreateEngine(fakeService);
        var model = CreateLocalModel();

        await engine.StartRecordingAsync(model);
        // No Task.Delay — duration is well under MinRecordingSeconds (0.5s),
        // so the short-recording branch fires and TranscribeAsync is never called.
        await engine.StopAndTranscribeAsync();

        fakeService.CapturedPath.Should().BeNull(
            "short-recording branch must not invoke the pipeline");

        // Since we cannot capture the path through the service, diff the
        // Recordings directory to confirm no rec_*.wav file leaked.
        var filesAfter = Directory.GetFiles(_recordingsDir, "rec_*.wav").ToHashSet();
        var newFiles = filesAfter.Except(_preExistingFiles).ToList();
        foreach (var p in newFiles) _touchedPaths.Add(p);

        newFiles.Should().BeEmpty(
            "engine must delete the WAV immediately on the short-recording path");
    }

    // ── Engine construction helper ────────────────────────────────────────

    /// <summary>
    /// Builds a real <see cref="VoxScriptEngine"/> wired to a real
    /// <see cref="TranscriptionPipeline"/> and <see cref="TranscriptionServiceRegistry"/>,
    /// substituting only interfaces. Mirrors the pattern in
    /// <c>VoxScriptEngineTests.CreateEngine()</c> because
    /// <see cref="TranscriptionServiceRegistry"/>, <see cref="TranscriptionPipeline"/>,
    /// <see cref="WordReplacementService"/>, and <see cref="PowerModeSessionManager"/>
    /// are all sealed and cannot be mocked by NSubstitute.
    /// </summary>
    private VoxScriptEngine CreateEngine(ITranscriptionService fakeService)
    {
        var audio = Substitute.For<IAudioCaptureService>();

        // Registry with our fake file-based service registered for ModelProvider.Local.
        var registry = new TranscriptionServiceRegistry([fakeService], []);

        var wordReplacement = new WordReplacementService(
            Substitute.For<IWordReplacementRepository>(),
            Substitute.For<IVocabularyRepository>(),
            Substitute.For<ICommonWordList>());

        var powerModeManager = new PowerModeManager();
        var powerModeSession = new PowerModeSessionManager(
            powerModeManager,
            Substitute.For<IActiveWindowService>());

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

    private static TranscriptionModel CreateLocalModel() => new(
        Provider: ModelProvider.Local,
        Name: "test-model",
        DisplayName: "Test Model",
        SupportsStreaming: false,   // Forces registry to wrap in FileTranscriptionSession.
        IsLocal: true);

    // ── Disposable cleanup ────────────────────────────────────────────────

    public void Dispose()
    {
        // Clean up any WAV files the engine may have left behind on test failure.
        foreach (var p in _touchedPaths)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
        }

        // Also sweep the Recordings dir for any rec_*.wav files that appeared
        // during the test and are not in the pre-existing set (defensive — the
        // short-recording test could fail before we appended to _touchedPaths).
        try
        {
            foreach (var p in Directory.GetFiles(_recordingsDir, "rec_*.wav"))
            {
                if (_preExistingFiles.Contains(p)) continue;
                try { File.Delete(p); } catch { /* best-effort */ }
            }
        }
        catch { /* directory may not exist in degenerate cases */ }
    }

    // ── Test doubles ──────────────────────────────────────────────────────

    /// <summary>
    /// Fake <see cref="ITranscriptionService"/> that captures the audio path
    /// passed to <see cref="TranscribeAsync"/> and either returns
    /// <see cref="Result"/> or invokes <see cref="Throw"/> to simulate failure.
    /// </summary>
    private sealed class CapturingTranscriptionService : ITranscriptionService
    {
        public ModelProvider Provider => ModelProvider.Local;
        public string? CapturedPath { get; private set; }
        public string Result { get; set; } = string.Empty;
        public Action? Throw { get; set; }

        public Task<string> TranscribeAsync(
            string audioPath, ITranscriptionModel model, string? language, CancellationToken ct)
        {
            CapturedPath = audioPath;
            Throw?.Invoke();
            return Task.FromResult(Result);
        }
    }

    /// <summary>Minimal in-memory <see cref="ISettingsStore"/> for tests.</summary>
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }
}
