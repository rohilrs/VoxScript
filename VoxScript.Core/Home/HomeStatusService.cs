using VoxScript.Core.AI;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Core.Home;

public sealed class HomeStatusService : IHomeStatusService
{
    private readonly AppSettings _settings;
    private readonly IModelManager _modelManager;
    private readonly HttpClient _http;

    public HomeStatusService(AppSettings settings, IModelManager modelManager, HttpClient http)
    {
        _settings = settings;
        _modelManager = modelManager;
        _http = http;
    }

    public Task<StatusResult> GetModelStatusAsync(CancellationToken ct)
    {
        var modelName = _settings.SelectedModelName ?? "ggml-base.en";

        if (_modelManager.IsDownloaded(modelName))
            return Task.FromResult(new StatusResult(StatusLevel.Ready, modelName));

        if (_modelManager.IsDownloading(modelName))
            return Task.FromResult(new StatusResult(StatusLevel.Warming, $"Downloading {modelName}…"));

        return Task.FromResult(
            new StatusResult(StatusLevel.Unavailable, $"Model missing: {modelName}"));
    }

    public async Task<StatusResult> GetAiEnhancementStatusAsync(CancellationToken ct)
    {
        if (!_settings.AiEnhancementEnabled)
            return new StatusResult(StatusLevel.Off, "AI Enhancement off");

        if (_settings.AiProvider == AiProvider.Local)
            return await PingOllamaAsync(_settings.OllamaEndpoint, "AI Enhancement", ct);

        return new StatusResult(StatusLevel.Ready,
            _settings.AiProvider == AiProvider.OpenAI ? "OpenAI ready" : "Anthropic ready");
    }

    public async Task<StatusResult> GetLlmFormattingStatusAsync(CancellationToken ct)
    {
        if (!_settings.StructuralFormattingEnabled)
            return new StatusResult(StatusLevel.Off, "LLM Formatting off");

        if (_settings.StructuralAiProvider == AiProvider.Local)
            return await PingOllamaAsync(_settings.StructuralOllamaEndpoint, "LLM Formatting", ct);

        return new StatusResult(StatusLevel.Ready,
            _settings.StructuralAiProvider == AiProvider.OpenAI ? "OpenAI ready" : "Anthropic ready");
    }

    public StatusResult Rollup(params StatusResult[] components)
    {
        var active = components.Where(c => c.Level != StatusLevel.Off).ToList();

        if (active.Count == 0)
            return new StatusResult(StatusLevel.Ready, "Ready");

        if (active.Any(c => c.Level == StatusLevel.Unavailable))
            return new StatusResult(StatusLevel.Unavailable, "Component unavailable");

        if (active.Any(c => c.Level == StatusLevel.Warming))
            return new StatusResult(StatusLevel.Warming, "Warming up");

        return new StatusResult(StatusLevel.Ready, "Ready");
    }

    private async Task<StatusResult> PingOllamaAsync(
        string endpoint, string label, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var url = endpoint.TrimEnd('/') + "/api/tags";
            var response = await _http.GetAsync(url, cts.Token);

            return response.IsSuccessStatusCode
                ? new StatusResult(StatusLevel.Ready, $"{label}: Ollama connected")
                : new StatusResult(StatusLevel.Unavailable, $"{label}: Ollama error");
        }
        catch
        {
            return new StatusResult(StatusLevel.Unavailable, $"{label}: Ollama unavailable");
        }
    }
}
