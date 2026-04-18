using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VoxScript.Core.AI;

namespace VoxScript.Tests.AI;

public class AiCompleterTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static (AiCompleter sut, List<HttpRequestMessage> captured) BuildSut(
        HttpStatusCode status, string jsonBody)
    {
        var captured = new List<HttpRequestMessage>();
        var handler = new FakeHandler(status, jsonBody, captured);
        var http = new HttpClient(handler);
        return (new AiCompleter(http), captured);
    }

    // ── OpenAI ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_OpenAI_sends_correct_url_and_bearer_header()
    {
        var body = """{"choices":[{"message":{"content":"hello"}}]}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.OpenAI, "gpt-4o-mini",
            "http://localhost:11434", "sk-test");

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured.Should().HaveCount(1);
        captured[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
        captured[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured[0].Headers.Authorization!.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public async Task CompleteAsync_OpenAI_returns_content_string()
    {
        var body = """{"choices":[{"message":{"content":"structured output"}}]}""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.OpenAI, "gpt-4o-mini",
            "http://localhost:11434", "sk-test");

        var result = await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        result.Should().Be("structured output");
    }

    [Fact]
    public async Task CompleteAsync_OpenAI_throws_on_non_success()
    {
        var (sut, _) = BuildSut(HttpStatusCode.Unauthorized, "{}");
        var config = new AiCompletionConfig(AiProvider.OpenAI, "gpt-4o-mini",
            "http://localhost:11434", "bad-key");

        var act = () => sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Anthropic ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_Anthropic_sends_correct_url_and_api_key_header()
    {
        var body = """{"content":[{"text":"anthropic response"}]}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Anthropic, "claude-haiku-4-5-20251001",
            "http://localhost:11434", "anthro-key");

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured[0].RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        captured[0].Headers.GetValues("x-api-key").First().Should().Be("anthro-key");
    }

    [Fact]
    public async Task CompleteAsync_Anthropic_returns_content_text()
    {
        var body = """{"content":[{"text":"formatted text"}]}""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Anthropic, "claude-haiku-4-5-20251001",
            "http://localhost:11434", "anthro-key");

        var result = await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        result.Should().Be("formatted text");
    }

    // ── Ollama ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_Ollama_sends_to_configured_endpoint()
    {
        var body = """{"message":{"content":"local response"}}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434", null);

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured[0].RequestUri!.ToString().Should().Be("http://localhost:11434/api/chat");
    }

    [Fact]
    public async Task CompleteAsync_Ollama_returns_message_content()
    {
        var body = """{"message":{"content":"list formatted"}}""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434", null);

        var result = await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        result.Should().Be("list formatted");
    }

    [Fact]
    public async Task CompleteAsync_Ollama_strips_trailing_slash_from_endpoint()
    {
        var body = """{"message":{"content":"ok"}}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434/", null);

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        captured[0].RequestUri!.ToString().Should().Be("http://localhost:11434/api/chat");
    }

    [Fact]
    public async Task CompleteAsync_Ollama_sends_keep_alive_negative_one()
    {
        var body = """{"message":{"content":"ok"}}""";
        var (sut, captured) = BuildSut(HttpStatusCode.OK, body);
        var config = new AiCompletionConfig(AiProvider.Local, "qwen2.5:3b",
            "http://localhost:11434", null);

        await sut.CompleteAsync(config, "sys", "user", CancellationToken.None);

        var requestBody = await captured[0].Content!.ReadAsStringAsync();
        // -1 tells Ollama to keep the model loaded for the rest of the process
        requestBody.Should().Contain("\"keep_alive\":-1");
    }
}

// ── Test infrastructure ───────────────────────────────────────────────────

internal sealed class FakeHandler(
    HttpStatusCode status,
    string body,
    List<HttpRequestMessage> captured) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Clone the request so headers/content are readable after disposal
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var h in request.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (request.Content is not null)
            clone.Content = new StringContent(await request.Content.ReadAsStringAsync(cancellationToken));
        captured.Add(clone);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
