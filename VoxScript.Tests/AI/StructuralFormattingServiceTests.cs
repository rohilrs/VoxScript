using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VoxScript.Core.AI;
using VoxScript.Core.Settings;

namespace VoxScript.Tests.AI;

public class StructuralFormattingServiceTests
{
    private static (StructuralFormattingService sut, IAiCompleter completer)
        BuildSut(AiProvider provider = AiProvider.Local,
                 string model = "qwen2.5:3b",
                 string? apiKey = null)
    {
        var completer = Substitute.For<IAiCompleter>();
        var keyStore  = Substitute.For<IApiKeyStore>();
        keyStore.GetKey("Structural.OpenAI").Returns(
            provider == AiProvider.OpenAI ? apiKey : null);
        keyStore.GetKey("Structural.Anthropic").Returns(
            provider == AiProvider.Anthropic ? apiKey : null);

        var keyManager = new ApiKeyManager(keyStore);
        var settings   = new AppSettings(new InMemorySettingsStore());
        settings.StructuralAiProvider  = provider;
        settings.StructuralAiModel     = model;
        settings.StructuralFormattingEnabled = true;

        var sut = new StructuralFormattingService(completer, keyManager, settings);
        return (sut, completer);
    }

    // ── IsConfigured ──────────────────────────────────────────────────────

    [Fact]
    public void IsConfigured_local_is_always_true()
    {
        var (sut, _) = BuildSut(AiProvider.Local);
        sut.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_OpenAI_false_when_no_key()
    {
        var (sut, _) = BuildSut(AiProvider.OpenAI, apiKey: null);
        sut.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_OpenAI_true_when_key_present()
    {
        var (sut, _) = BuildSut(AiProvider.OpenAI, apiKey: "sk-test-key");
        sut.IsConfigured.Should().BeTrue();
    }

    // ── FormatAsync: early exits ───────────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_when_not_configured()
    {
        var (sut, completer) = BuildSut(AiProvider.OpenAI, apiKey: null);

        var result = await sut.FormatAsync("some text", CancellationToken.None);

        result.Should().BeNull();
        await completer.DidNotReceive().CompleteAsync(
            Arg.Any<AiCompletionConfig>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FormatAsync_returns_null_for_empty_input(string? input)
    {
        var (sut, completer) = BuildSut(AiProvider.Local);

        var result = await sut.FormatAsync(input!, CancellationToken.None);

        result.Should().BeNull();
        await completer.DidNotReceive().CompleteAsync(
            Arg.Any<AiCompletionConfig>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── FormatAsync: success path ──────────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_LLM_output_when_validator_accepts()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        // Input and output have the same content words; the LLM only added "1." "2." "3."
        // list markers, which the validator excludes from its ratio check.
        const string input     = "fix auth improve logging deployment script";
        const string llmOutput = "1. fix auth\n2. improve logging\n3. deployment script";
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(llmOutput);

        var result = await sut.FormatAsync(input, CancellationToken.None);

        result.Should().Be(llmOutput);
    }

    // ── FormatAsync: validator rejects ────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_when_validator_rejects_output()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        const string input     = "hello world foo bar baz";         // 5 content words
        const string llmOutput = "completely different hallucinated words added added added added added added added"; // >> 1.15 ratio
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(llmOutput);

        var result = await sut.FormatAsync(input, CancellationToken.None);

        result.Should().BeNull();
    }

    // ── FormatAsync: HTTP failure ─────────────────────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_on_HttpRequestException()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await sut.FormatAsync("some text here", CancellationToken.None);

        result.Should().BeNull();
    }

    // ── FormatAsync: external cancellation propagates ────────────────────

    [Fact]
    public async Task FormatAsync_propagates_OperationCanceledException_from_external_ct()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The completer throws OCE when the external token is already cancelled
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Is<CancellationToken>(t => t.IsCancellationRequested))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => sut.FormatAsync("some text here", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── FormatAsync: internal timeout returns null ─────────────────────────

    [Fact]
    public async Task FormatAsync_returns_null_on_internal_timeout()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        // Simulate the internal timeout timer firing: throw OCE with a token that is NOT
        // the external token. The service must distinguish this from a user cancel.
        using var externalCts = new CancellationTokenSource();
        using var internalCts = new CancellationTokenSource();
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(internalCts.Token));

        var result = await sut.FormatAsync("some text here", externalCts.Token);

        result.Should().BeNull();
    }
}

// ── In-memory settings store for tests ───────────────────────────────────

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object?> _data = new();
    public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
    public void Set<T>(string key, T value) => _data[key] = value;
    public bool Contains(string key) => _data.ContainsKey(key);
    public void Remove(string key) => _data.Remove(key);
}
