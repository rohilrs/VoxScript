using FluentAssertions;
using NSubstitute;
using VoxScript.Core.AI;
using VoxScript.Core.Dictionary;
using VoxScript.Core.History;
using VoxScript.Core.Persistence;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Core.Transcription.Processing;
using VoxScript.Tests.AI; // InMemorySettingsStore (internal, same assembly)

namespace VoxScript.Tests.Transcription;

public class TranscriptionPipelineStructuralTests
{
    private static (TranscriptionPipeline pipeline,
                    IStructuralFormattingService structuralSvc,
                    ITranscriptionRepository repo,
                    AppSettings settings)
        BuildPipeline(bool structuralEnabled, bool structuralConfigured)
    {
        var structuralSvc = Substitute.For<IStructuralFormattingService>();
        structuralSvc.IsConfigured.Returns(structuralConfigured);

        var aiEnhancement = Substitute.For<IAIEnhancementService>();
        aiEnhancement.IsConfigured.Returns(false);

        var repo = Substitute.For<ITranscriptionRepository>();
        var wordReplacementRepo = Substitute.For<IWordReplacementRepository>();
        wordReplacementRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        var vocabRepo = Substitute.For<IVocabularyRepository>();
        var autoVocab = Substitute.For<IAutoVocabularyService>();

        // PowerModeSessionManager is sealed — construct a real one with a real
        // (empty) PowerModeManager and a stubbed IActiveWindowService.
        var powerMode = new PowerModeSessionManager(
            new PowerModeManager(),
            Substitute.For<IActiveWindowService>());

        var settingsStore = new InMemorySettingsStore();
        var settings = new AppSettings(settingsStore);
        settings.SmartFormattingEnabled = false;       // isolate structural step
        settings.StructuralFormattingEnabled = structuralEnabled;

        var pipeline = new TranscriptionPipeline(
            new TranscriptionOutputFilter(),
            new SmartTextFormatter(),
            new WordReplacementService(wordReplacementRepo, vocabRepo),
            aiEnhancement,
            repo,
            powerMode,
            autoVocab,
            settings,
            structuralSvc); // new last parameter

        return (pipeline, structuralSvc, repo, settings);
    }

    private static ITranscriptionSession FakeSession(string text)
    {
        var session = Substitute.For<ITranscriptionSession>();
        session.TranscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(text);
        session.Model.Returns(new TranscriptionModel(
            ModelProvider.Local,
            Name: "base.en",
            DisplayName: "base.en",
            SupportsStreaming: false,
            IsLocal: true));
        return session;
    }

    [Fact]
    public async Task Pipeline_skips_structural_when_setting_disabled()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: false, structuralConfigured: true);
        var session = FakeSession("some transcribed text here");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await structuralSvc.DidNotReceive()
            .FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pipeline_skips_structural_when_not_configured()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: false);
        var session = FakeSession("some transcribed text here");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await structuralSvc.DidNotReceive()
            .FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pipeline_calls_structural_when_enabled_and_configured()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var session = FakeSession("some transcribed text here");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await structuralSvc.Received(1)
            .FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pipeline_uses_rule_based_output_when_structural_returns_null()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var session = FakeSession("hello world");

        var result = await pipeline.RunAsync(session, "file.wav", 1.0, false, CancellationToken.None);

        result.Should().Be("Hello world");
    }

    [Fact]
    public async Task Pipeline_uses_structured_output_when_structural_returns_text()
    {
        var (pipeline, structuralSvc, _, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        const string structured = "1. Fix auth\n2. Improve logging\n3. Fix deployment";
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(structured);
        var session = FakeSession("first fix auth second improve logging third fix deployment");

        var result = await pipeline.RunAsync(session, "file.wav", 3.0, false, CancellationToken.None);

        result.Should().Be(structured);
    }

    [Fact]
    public async Task Pipeline_persists_structured_text_in_Text_column()
    {
        var (pipeline, structuralSvc, repo, _) = BuildPipeline(
            structuralEnabled: true, structuralConfigured: true);
        const string structured = "1. Fix auth\n2. Improve logging";
        structuralSvc.FormatAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(structured);
        var session = FakeSession("first fix auth second improve logging");

        await pipeline.RunAsync(session, "file.wav", 2.0, false, CancellationToken.None);

        await repo.Received(1).AddAsync(
            Arg.Is<TranscriptionRecord>(r => r.Text == structured),
            Arg.Any<CancellationToken>());
    }
}
