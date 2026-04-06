// VoxScript.Core/Transcription/Streaming/ElevenLabsStreamingProvider.cs
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Streaming;

public sealed class ElevenLabsStreamingProvider : IStreamingProvider
{
    private readonly ApiKeyManager _keys;
    private ClientWebSocket? _ws;
    private readonly Channel<StreamingEvent> _events =
        System.Threading.Channels.Channel.CreateUnbounded<StreamingEvent>();
    private Task? _receiveTask;
    private string _finalTranscript = string.Empty;

    public ModelProvider Provider => ModelProvider.ElevenLabs;
    public IAsyncEnumerable<StreamingEvent> Events => ReadEvents();

    public ElevenLabsStreamingProvider(ApiKeyManager keys) => _keys = keys;

    public async Task ConnectAsync(ITranscriptionModel model, string? language, CancellationToken ct)
    {
        var key = _keys.GetElevenLabsKey()
            ?? throw new InvalidOperationException("ElevenLabs API key not configured.");

        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("xi-api-key", key);

        await _ws.ConnectAsync(
            new Uri("wss://api.elevenlabs.io/v1/speech-to-text/stream"), ct);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(ct), ct);
    }

    public async Task SendChunkAsync(byte[] pcmInt16Chunk, int byteCount, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;

        // ElevenLabs expects base64-encoded audio in JSON
        var payload = JsonSerializer.Serialize(new
        {
            audio = Convert.ToBase64String(pcmInt16Chunk, 0, byteCount),
            audio_format = "pcm_s16le",
            sample_rate = 16000,
        });
        await _ws.SendAsync(
            System.Text.Encoding.UTF8.GetBytes(payload),
            WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async Task<string> CommitAsync(CancellationToken ct)
    {
        if (_ws?.State == WebSocketState.Open)
        {
            var eos = JsonSerializer.Serialize(new { type = "end_of_stream" });
            await _ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(eos),
                WebSocketMessageType.Text, true, ct);
        }
        if (_receiveTask is not null)
            await _receiveTask;
        return _finalTranscript.Trim();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16384];
        var sb = new System.Text.StringBuilder();

        while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) break;

            sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var json = sb.ToString();
            sb.Clear();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("text", out var textProp)) continue;

                var text = textProp.GetString() ?? string.Empty;
                bool isFinal = root.TryGetProperty("is_final", out var fin) && fin.GetBoolean();

                if (isFinal)
                {
                    _finalTranscript += " " + text;
                    _events.Writer.TryWrite(new StreamingEvent(StreamingEventKind.Final, text));
                }
                else
                {
                    _events.Writer.TryWrite(new StreamingEvent(StreamingEventKind.Partial, text));
                }
            }
            catch { /* malformed JSON */ }
        }
        _events.Writer.TryComplete();
    }

    private async IAsyncEnumerable<StreamingEvent> ReadEvents(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var ev in _events.Reader.ReadAllAsync(ct))
            yield return ev;
    }

    public async Task DisconnectAsync()
    {
        if (_ws?.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        _ws?.Dispose();
    }
}
