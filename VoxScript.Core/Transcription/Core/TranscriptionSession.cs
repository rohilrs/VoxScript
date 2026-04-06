using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

/// <summary>File-based (non-streaming) transcription session.</summary>
public sealed class FileTranscriptionSession : ITranscriptionSession
{
    private readonly ITranscriptionService _service;

    public FileTranscriptionSession(ITranscriptionModel model, ITranscriptionService service)
    {
        Model = model;
        _service = service;
    }

    public ITranscriptionModel Model { get; }
    public bool IsStreaming => false;

    public Task<Action<byte[], int>?> PrepareAsync(CancellationToken ct) =>
        Task.FromResult<Action<byte[], int>?>(null);

    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct) =>
        _service.TranscribeAsync(audioFilePath, Model, null, ct);

    public Task CancelAsync() => Task.CompletedTask;
}

/// <summary>Streaming transcription session — wraps IStreamingProvider.</summary>
public sealed class StreamingTranscriptionSession : ITranscriptionSession
{
    private readonly IStreamingProvider _provider;
    private readonly List<byte[]> _preConnectionBuffer = new();
    private bool _providerConnected;

    public StreamingTranscriptionSession(ITranscriptionModel model, IStreamingProvider provider)
    {
        Model = model;
        _provider = provider;
    }

    public ITranscriptionModel Model { get; }
    public bool IsStreaming => true;

    public async Task<Action<byte[], int>?> PrepareAsync(CancellationToken ct)
    {
        await _provider.ConnectAsync(Model, null, ct);
        _providerConnected = true;

        // Flush any buffered chunks captured before connection was ready
        foreach (var chunk in _preConnectionBuffer)
            await _provider.SendChunkAsync(chunk, chunk.Length, ct);
        _preConnectionBuffer.Clear();

        return async (bytes, count) =>
        {
            var copy = new byte[count];
            Array.Copy(bytes, copy, count);
            if (_providerConnected)
                await _provider.SendChunkAsync(copy, count, ct);
            else
                _preConnectionBuffer.Add(copy);
        };
    }

    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct) =>
        _provider.CommitAsync(ct);

    public async Task CancelAsync()
    {
        _providerConnected = false;
        await _provider.DisconnectAsync();
    }
}
