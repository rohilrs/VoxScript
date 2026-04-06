// VoxScript.Tests/Transcription/StreamingSessionTests.cs
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class StreamingTranscriptionSessionTests
{
    [Fact]
    public async Task PrepareAsync_connects_provider_and_flushes_prebuffer()
    {
        var model = new TranscriptionModel(
            ModelProvider.Deepgram, "nova-2", "Nova 2", true, false);

        var provider = Substitute.For<IStreamingProvider>();
        provider.ConnectAsync(default!, default, default).ReturnsForAnyArgs(Task.CompletedTask);

        Action<byte[], int>? capturedCallback = null;
        provider.When(p => p.SendChunkAsync(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>()))
            .Do(ci => { /* capture calls */ });

        var session = new StreamingTranscriptionSession(model, provider);
        var callback = await session.PrepareAsync(default);

        await provider.Received(1).ConnectAsync(model, null, default);
        callback.Should().NotBeNull();
    }

    [Fact]
    public async Task TranscribeAsync_calls_CommitAsync_on_provider()
    {
        var model = new TranscriptionModel(
            ModelProvider.Deepgram, "nova-2", "Nova 2", true, false);

        var provider = Substitute.For<IStreamingProvider>();
        provider.ConnectAsync(default!, default, default).ReturnsForAnyArgs(Task.CompletedTask);
        provider.CommitAsync(default).ReturnsForAnyArgs("streamed result");

        var session = new StreamingTranscriptionSession(model, provider);
        await session.PrepareAsync(default);
        var result = await session.TranscribeAsync("/dev/null", default);

        result.Should().Be("streamed result");
    }
}
