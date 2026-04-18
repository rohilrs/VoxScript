using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VoxScript.Core.AI;

public sealed class AiCompleter(HttpClient http) : IAiCompleter
{
    public Task<string> CompleteAsync(
        AiCompletionConfig config,
        string systemPrompt,
        string userMessage,
        CancellationToken ct) => config.Provider switch
        {
            AiProvider.OpenAI => CompleteOpenAiAsync(
                config.ApiKey ?? throw new InvalidOperationException("OpenAI API key not set"),
                config.Model, systemPrompt, userMessage, ct),

            AiProvider.Anthropic => CompleteAnthropicAsync(
                config.ApiKey ?? throw new InvalidOperationException("Anthropic API key not set"),
                config.Model, systemPrompt, userMessage, ct),

            _ => CompleteOllamaAsync(config.OllamaEndpoint, config.Model, systemPrompt, userMessage, ct),
        };

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

        var response = await http.SendAsync(request, ct);
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

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string> CompleteOllamaAsync(string endpoint, string model,
        string systemPrompt, string userMessage, CancellationToken ct)
    {
        var baseUrl = endpoint.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat");
        request.Content = JsonContent.Create(new
        {
            model,
            stream = false,
            keep_alive = -1, // keep model resident in VRAM for the rest of the process
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            }
        });

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
