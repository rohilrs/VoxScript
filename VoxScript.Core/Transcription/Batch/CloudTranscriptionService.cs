// VoxScript.Core/Transcription/Batch/CloudTranscriptionService.cs
using System.Net.Http.Headers;
using System.Text.Json;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Batch;

public sealed class CloudTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _http;
    private readonly ApiKeyManager _keys;

    public ModelProvider Provider => ModelProvider.OpenAI;

    public CloudTranscriptionService(HttpClient http, ApiKeyManager keys)
    {
        _http = http;
        _keys = keys;
    }

    public async Task<string> TranscribeAsync(string audioPath, ITranscriptionModel model,
        string? language, CancellationToken ct)
    {
        var key = _keys.GetOpenAiKey()
            ?? throw new InvalidOperationException("OpenAI API key not configured.");

        await using var audioStream = File.OpenRead(audioPath);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") }
        }, "file", Path.GetFileName(audioPath));
        content.Add(new StringContent(model.Name), "model");
        if (language is not null)
            content.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = content;

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }
}
