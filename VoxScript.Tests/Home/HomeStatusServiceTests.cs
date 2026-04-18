using FluentAssertions;
using NSubstitute;
using VoxScript.Core.AI;
using VoxScript.Core.Home;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Tests.Home;

public class HomeStatusServiceTests
{
    private static (HomeStatusService svc, ISettingsStore store, IModelManager modelMgr)
        Build(HttpClient? http = null)
    {
        var store = Substitute.For<ISettingsStore>();
        store.Get<bool?>(nameof(AppSettings.AiEnhancementEnabled)).Returns((bool?)false);
        store.Get<bool?>(nameof(AppSettings.StructuralFormattingEnabled)).Returns((bool?)false);
        store.Get<AiProvider?>(nameof(AppSettings.AiProvider)).Returns((AiProvider?)AiProvider.Local);
        store.Get<AiProvider?>(nameof(AppSettings.StructuralAiProvider)).Returns((AiProvider?)AiProvider.Local);
        store.Get<string>(nameof(AppSettings.SelectedModelName)).Returns("ggml-base.en");
        store.Get<string>(nameof(AppSettings.OllamaEndpoint)).Returns("http://localhost:11434");
        store.Get<string>(nameof(AppSettings.StructuralOllamaEndpoint)).Returns("http://localhost:11434");

        var settings = new AppSettings(store);
        var modelMgr = Substitute.For<IModelManager>();
        var httpClient = http ?? new HttpClient();
        var svc = new HomeStatusService(settings, modelMgr, httpClient);
        return (svc, store, modelMgr);
    }

    [Fact]
    public async Task GetModelStatus_ready_when_file_exists()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(true);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public async Task GetModelStatus_warming_when_download_in_progress()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(false);
        modelMgr.IsDownloading("ggml-base.en").Returns(true);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Warming);
    }

    [Fact]
    public async Task GetModelStatus_unavailable_when_file_missing_and_not_downloading()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(false);
        modelMgr.IsDownloading("ggml-base.en").Returns(false);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Unavailable);
    }

    [Fact]
    public async Task GetModelStatus_label_includes_model_name_when_unavailable()
    {
        var (svc, _, modelMgr) = Build();
        modelMgr.IsDownloaded("ggml-base.en").Returns(false);
        modelMgr.IsDownloading("ggml-base.en").Returns(false);

        var result = await svc.GetModelStatusAsync(CancellationToken.None);

        result.Label.Should().Contain("ggml-base.en");
    }

    [Fact]
    public async Task GetAiEnhancementStatus_off_when_disabled()
    {
        var (svc, store, _) = Build();
        store.Get<bool?>(nameof(AppSettings.AiEnhancementEnabled)).Returns((bool?)false);

        var result = await svc.GetAiEnhancementStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Off);
    }

    [Fact]
    public async Task GetAiEnhancementStatus_ready_when_cloud_provider_selected()
    {
        var (svc, store, _) = Build();
        store.Get<bool?>(nameof(AppSettings.AiEnhancementEnabled)).Returns((bool?)true);
        store.Get<AiProvider?>(nameof(AppSettings.AiProvider)).Returns((AiProvider?)AiProvider.OpenAI);

        var result = await svc.GetAiEnhancementStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public async Task GetLlmFormattingStatus_off_when_disabled()
    {
        var (svc, store, _) = Build();
        store.Get<bool?>(nameof(AppSettings.StructuralFormattingEnabled)).Returns((bool?)false);

        var result = await svc.GetLlmFormattingStatusAsync(CancellationToken.None);

        result.Level.Should().Be(StatusLevel.Off);
    }

    [Fact]
    public void Rollup_all_ready_returns_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Ready, "AI"));
        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public void Rollup_unavailable_beats_warning_and_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Warming, "AI"),
            new StatusResult(StatusLevel.Unavailable, "LLM"));
        result.Level.Should().Be(StatusLevel.Unavailable);
    }

    [Fact]
    public void Rollup_warming_beats_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Warming, "AI"));
        result.Level.Should().Be(StatusLevel.Warming);
    }

    [Fact]
    public void Rollup_off_components_are_skipped()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Ready, "Model"),
            new StatusResult(StatusLevel.Off, "AI"),
            new StatusResult(StatusLevel.Off, "LLM"));
        result.Level.Should().Be(StatusLevel.Ready);
    }

    [Fact]
    public void Rollup_all_off_returns_ready()
    {
        var (svc, _, _) = Build();
        var result = svc.Rollup(
            new StatusResult(StatusLevel.Off, "AI"),
            new StatusResult(StatusLevel.Off, "LLM"));
        result.Level.Should().Be(StatusLevel.Ready);
    }
}
