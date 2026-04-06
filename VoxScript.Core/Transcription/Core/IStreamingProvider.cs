using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

public enum StreamingEventKind { Partial, Final, Error }

public sealed record StreamingEvent(StreamingEventKind Kind, string Text, string? ErrorMessage = null);

public interface IStreamingProvider
{
    ModelProvider Provider { get; }
    Task ConnectAsync(ITranscriptionModel model, string? language, CancellationToken ct);
    Task SendChunkAsync(byte[] pcmInt16Chunk, int byteCount, CancellationToken ct);
    Task<string> CommitAsync(CancellationToken ct);
    IAsyncEnumerable<StreamingEvent> Events { get; }
    Task DisconnectAsync();
}
