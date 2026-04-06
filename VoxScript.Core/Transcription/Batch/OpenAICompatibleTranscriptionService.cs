// VoxScript.Core/Transcription/Batch/OpenAICompatibleTranscriptionService.cs
using System.Net.Http.Headers;
using System.Text.Json;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Batch;

/// <summary>Sends to any OpenAI-compatible /audio/transcriptions endpoint.</summary>
public sealed class OpenAICompatibleTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _apiKey;

    public ModelProvider Provider => ModelProvider.OpenAICompatible;

    public OpenAICompatibleTranscriptionService(HttpClient http, string baseUrl, string? apiKey)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public async Task<string> TranscribeAsync(string audioPath, ITranscriptionModel model,
        string? language, CancellationToken ct)
    {
        await using var audioStream = File.OpenRead(audioPath);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") }
        }, "file", Path.GetFileName(audioPath));
        content.Add(new StringContent(model.Name), "model");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_baseUrl}/audio/transcriptions");
        if (_apiKey is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = content;

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }
}
