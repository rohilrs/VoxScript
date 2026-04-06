using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class AIService
{
    private readonly HttpClient _http;
    private readonly ApiKeyManager _keyManager;
    private readonly AppSettings _settings;

    public bool IsConfigured => _settings.AiProvider switch
    {
        AiProvider.OpenAI => _keyManager.GetOpenAiKey() is not null,
        AiProvider.Anthropic => _keyManager.GetAnthropicKey() is not null,
        AiProvider.Local => true, // Ollama doesn't need a key
        _ => false,
    };

    public AIService(HttpClient http, ApiKeyManager keyManager, AppSettings settings)
    {
        _http = http;
        _keyManager = keyManager;
        _settings = settings;
    }

    public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var model = _settings.AiModelName;

        return _settings.AiProvider switch
        {
            AiProvider.OpenAI => CompleteOpenAiAsync(
                _keyManager.GetOpenAiKey() ?? throw new InvalidOperationException("OpenAI API key not set"),
                model, systemPrompt, userMessage, ct),

            AiProvider.Anthropic => CompleteAnthropicAsync(
                _keyManager.GetAnthropicKey() ?? throw new InvalidOperationException("Anthropic API key not set"),
                model, systemPrompt, userMessage, ct),

            _ => CompleteOllamaAsync(model, systemPrompt, userMessage, ct),
        };
    }

    private async Task<string> CompleteOpenAiAsync(string apiKey, string model,
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            },
            max_tokens = 2048,
        });

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CompleteAnthropicAsync(string apiKey, string model,
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userMessage } },
            max_tokens = 2048,
        });

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CompleteOllamaAsync(string model, string systemPrompt,
        string userMessage, CancellationToken ct)
    {
        var endpoint = _settings.OllamaEndpoint.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{endpoint}/api/chat");
        request.Content = JsonContent.Create(new
        {
            model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            }
        });

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
