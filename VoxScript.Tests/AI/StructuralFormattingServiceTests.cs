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

    // ── FormatAsync: short input is skipped without calling LLM ───────────

    [Theory]
    [InlineData("hi")]                                      // 1 word
    [InlineData("turn the lights on")]                      // 4 words
    [InlineData("send a message to alice about lunch")]     // 7 words — still under 10
    public async Task FormatAsync_skips_LLM_for_short_input(string input)
    {
        var (sut, completer) = BuildSut(AiProvider.Local);

        var result = await sut.FormatAsync(input, CancellationToken.None);

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
        // Input has 14 content words. LLM output preserves all 14 and only adds
        // "1." "2." "3." list markers (which the validator excludes from its
        // ratio check). Result: ratio = 1.0, validator accepts.
        // Input length (14) clears the short-input skip threshold (10).
        const string input     = "first we fix the bug second we improve logging third we deploy the script";
        const string llmOutput = "1. first we fix the bug\n2. second we improve logging\n3. third we deploy the script";
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
        // 10 content words — clears the short-input skip — so we exercise the validator path.
        const string input     = "hello world foo bar baz qux quux corge grault garply";
        // ~30 words — ratio ~3.0, well above the 1.15 upper bound.
        const string llmOutput = "completely different hallucinated words added added added added added added added "
                               + "added added added added added added added added added added added added added "
                               + "added added added added added";
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

        var result = await sut.FormatAsync("this is a long enough piece of text with at least ten content words", CancellationToken.None);

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

        var act = () => sut.FormatAsync("this is a long enough piece of text with at least ten content words", cts.Token);

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

        var result = await sut.FormatAsync("this is a long enough piece of text with at least ten content words", externalCts.Token);

        result.Should().BeNull();
    }

    // ── WarmupAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task WarmupAsync_does_nothing_when_not_configured()
    {
        var (sut, completer) = BuildSut(AiProvider.OpenAI, apiKey: null);

        await sut.WarmupAsync();

        await completer.DidNotReceive().CompleteAsync(
            Arg.Any<AiCompletionConfig>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarmupAsync_skips_cloud_providers()
    {
        var (sut, completer) = BuildSut(AiProvider.OpenAI, apiKey: "sk-test");

        await sut.WarmupAsync();

        await completer.DidNotReceive().CompleteAsync(
            Arg.Any<AiCompletionConfig>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarmupAsync_calls_completer_for_local_provider()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ok");

        await sut.WarmupAsync();

        await completer.Received(1).CompleteAsync(
            Arg.Is<AiCompletionConfig>(c => c.Provider == AiProvider.Local),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WarmupAsync_swallows_completer_errors()
    {
        var (sut, completer) = BuildSut(AiProvider.Local);
        completer.CompleteAsync(Arg.Any<AiCompletionConfig>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var act = () => sut.WarmupAsync();

        await act.Should().NotThrowAsync();
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
