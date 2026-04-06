// VoxScript.Core/Transcription/Streaming/DeepgramStreamingProvider.cs
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Streaming;

public sealed class DeepgramStreamingProvider : IStreamingProvider
{
    private readonly ApiKeyManager _keys;
    private ClientWebSocket? _ws;
    private CancellationToken _ct;
    private readonly Channel<StreamingEvent> _events =
        System.Threading.Channels.Channel.CreateUnbounded<StreamingEvent>();
    private Task? _receiveTask;
    private string _finalTranscript = string.Empty;

    public ModelProvider Provider => ModelProvider.Deepgram;
    public IAsyncEnumerable<StreamingEvent> Events => ReadEvents();

    public DeepgramStreamingProvider(ApiKeyManager keys) => _keys = keys;

    public async Task ConnectAsync(ITranscriptionModel model, string? language, CancellationToken ct)
    {
        var key = _keys.GetDeepgramKey()
            ?? throw new InvalidOperationException("Deepgram API key not configured.");

        _ct = ct;
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Token {key}");

        var lang = language ?? "en-US";
        var uri = new Uri($"wss://api.deepgram.com/v1/listen" +
            $"?model=nova-2&language={lang}&encoding=linear16&sample_rate=16000" +
            $"&channels=1&punctuate=true&interim_results=true");

        await _ws.ConnectAsync(uri, ct);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(ct), ct);
    }

    public async Task SendChunkAsync(byte[] pcmInt16Chunk, int byteCount, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        await _ws.SendAsync(new ArraySegment<byte>(pcmInt16Chunk, 0, byteCount),
            WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async Task<string> CommitAsync(CancellationToken ct)
    {
        if (_ws?.State == WebSocketState.Open)
        {
            // Send close-stream message
            await _ws.SendAsync(
                System.Text.Encoding.UTF8.GetBytes("""{"type":"CloseStream"}"""),
                WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        if (_receiveTask is not null)
            await _receiveTask;
        return _finalTranscript;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
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
                if (!root.TryGetProperty("channel", out var channel)) continue;

                var transcript = channel
                    .GetProperty("alternatives")[0]
                    .GetProperty("transcript")
                    .GetString() ?? string.Empty;

                bool isFinal = root.TryGetProperty("is_final", out var isFinalProp)
                            && isFinalProp.GetBoolean();

                if (isFinal && !string.IsNullOrWhiteSpace(transcript))
                {
                    _finalTranscript += " " + transcript;
                    _events.Writer.TryWrite(new StreamingEvent(StreamingEventKind.Final, transcript));
                }
                else if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _events.Writer.TryWrite(new StreamingEvent(StreamingEventKind.Partial, transcript));
                }
            }
            catch { /* malformed JSON -- continue */ }
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
        _ws = null;
    }
}
