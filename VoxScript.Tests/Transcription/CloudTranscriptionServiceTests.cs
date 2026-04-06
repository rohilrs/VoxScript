// VoxScript.Tests/Transcription/CloudTranscriptionServiceTests.cs
using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Batch;
using VoxScript.Core.Transcription.Models;
using VoxScript.Tests.Helpers;
using Xunit;

namespace VoxScript.Tests.Transcription;

public class CloudTranscriptionServiceTests
{
    private static HttpClient BuildMockHttp(string transcriptText)
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { text = transcriptText }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        return new HttpClient(handler);
    }

    [Fact]
    public async Task TranscribeAsync_returns_text_from_api_response()
    {
        var keyStore = Substitute.For<IApiKeyStore>();
        keyStore.GetKey("OpenAI").Returns("test-key");
        var keys = new ApiKeyManager(keyStore);

        var svc = new CloudTranscriptionService(
            BuildMockHttp("Hello from cloud"), keys);

        // Write a minimal WAV file for testing
        var tmpPath = Path.GetTempFileName() + ".wav";
        File.WriteAllBytes(tmpPath, WavTestHelper.CreateSilenceWav(0.1));

        try
        {
            var model = new TranscriptionModel(ModelProvider.OpenAI,
                "whisper-1", "Whisper Cloud", false, false);
            var result = await svc.TranscribeAsync(tmpPath, model, null, default);
            result.Should().Be("Hello from cloud");
        }
        finally { File.Delete(tmpPath); }
    }
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    public MockHttpMessageHandler(HttpResponseMessage response) => _response = response;
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(_response);
}
