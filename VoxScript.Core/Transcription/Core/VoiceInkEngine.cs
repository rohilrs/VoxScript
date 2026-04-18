// VoxScript.Core/Transcription/Core/VoxScriptEngine.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using VoxScript.Core.Audio;
using VoxScript.Core.Platform;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

public sealed partial class VoxScriptEngine : ObservableObject
{
    private readonly IAudioCaptureService _audio;
    private readonly TranscriptionServiceRegistry _registry;
    private readonly TranscriptionPipeline _pipeline;
    private readonly AppSettings _settings;
    private readonly IPasteService _paste;
    private readonly ISoundEffectsService _sounds;
    private readonly IMediaControlService _media;

    private ITranscriptionSession? _activeSession;
    private string? _currentAudioPath;
    private DateTime _recordingStartTime;
    private CancellationTokenSource? _cts;

    // Buffered chunks captured before streaming provider connects
    private readonly List<(byte[] data, int count)> _preConnectBuffer = new();
    private FileStream? _wavStream;
    private bool _startingUp; // true while StartRecordingAsync is setting up the pipeline
    private bool _stopRequestedDuringStartup; // set if StopAndTranscribeAsync called during startup

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private float _audioLevel; // 0.0 to 1.0, RMS of current audio chunk

    [ObservableProperty]
    private bool _isToggleMode;

    public string? LastTranscription { get; private set; }

    public event EventHandler<string>? TranscriptionCompleted;
    public event EventHandler<string>? TranscriptionFailed;

    public VoxScriptEngine(
        IAudioCaptureService audio,
        TranscriptionServiceRegistry registry,
        TranscriptionPipeline pipeline,
        AppSettings settings,
        IPasteService paste,
        ISoundEffectsService sounds,
        IMediaControlService media)
    {
        _audio = audio;
        _registry = registry;
        _pipeline = pipeline;
        _settings = settings;
        _paste = paste;
        _sounds = sounds;
        _media = media;
    }

    public async Task ToggleRecordAsync(ITranscriptionModel model)
    {
        if (State == RecordingState.Recording)
            await StopAndTranscribeAsync();
        else if (State == RecordingState.Idle && !_startingUp)
            await StartRecordingAsync(model);
    }

    public async Task StartRecordingAsync(ITranscriptionModel model)
    {
        if (State != RecordingState.Idle || _startingUp) return;
        _startingUp = true;
        _stopRequestedDuringStartup = false;

        try
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Prepare audio file path
            var recordingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoxScript", "Recordings");
            Directory.CreateDirectory(recordingsDir);
            _currentAudioPath = Path.Combine(recordingsDir,
                $"rec_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav");

            _activeSession = _registry.CreateSession(model);
            _preConnectBuffer.Clear();

            // Phase 1: buffer-accumulating callback (used until streaming provider connects)
            Action<byte[], int> bufferChunk = (data, count) =>
            {
                var copy = new byte[count];
                Array.Copy(data, copy, count);
                _preConnectBuffer.Add((copy, count));
                AudioLevel = ComputeRms(data, count);
            };

            await _audio.StartAsync(_settings.AudioDeviceId, bufferChunk, ct);

            // Phase 2: prepare session (streaming providers connect here), then swap callback
            var chunkCallback = await _activeSession.PrepareAsync(ct);
            if (chunkCallback is not null)
            {
                // Flush buffer then switch to live streaming
                foreach (var (data, count) in _preConnectBuffer)
                    chunkCallback(data, count);
                _preConnectBuffer.Clear();
                // Restart capture with the real callback
                await _audio.StopAsync();
                await _audio.StartAsync(_settings.AudioDeviceId, chunkCallback, ct);
            }
            else
            {
                // File-based: restart capture writing to WAV file
                await _audio.StopAsync();
                _wavStream = new FileStream(_currentAudioPath, FileMode.Create, FileAccess.Write);
                WriteWavHeader(_wavStream); // placeholder header, finalized on stop
                // Write pre-buffered chunks
                foreach (var (data, count) in _preConnectBuffer)
                    _wavStream.Write(data, 0, count);
                _preConnectBuffer.Clear();
                await _audio.StartAsync(_settings.AudioDeviceId, (data, count) =>
                {
                    _wavStream?.Write(data, 0, count);
                    AudioLevel = ComputeRms(data, count);
                }, ct);
            }

            // Only transition to Recording after audio pipeline is fully set up.
            // This ensures the indicator/timer start at the right moment, and prevents
            // StopAndTranscribeAsync from running against a half-initialized pipeline
            // if the user releases the hotkey during setup.
            _recordingStartTime = DateTime.UtcNow;
            State = RecordingState.Recording;
            _sounds.PlayStart();
            if (_settings.PauseMediaWhileDictating)
                await _media.PauseMediaAsync();

            // If the user released the hotkey during startup, immediately stop.
            if (_stopRequestedDuringStartup)
            {
                _stopRequestedDuringStartup = false;
                await StopAndTranscribeAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start recording");
            await _audio.StopAsync();
            _wavStream?.Dispose();
            _wavStream = null;
            _activeSession = null;
            _cts?.Dispose();
            _cts = null;
            State = RecordingState.Idle;
        }
        finally
        {
            _startingUp = false;
        }
    }

    public async Task StopAndTranscribeAsync()
    {
        // If startup is still in progress, flag the deferred stop so
        // StartRecordingAsync handles it once the pipeline is ready.
        if (_startingUp)
        {
            _stopRequestedDuringStartup = true;
            return;
        }

        if (State != RecordingState.Recording || _activeSession is null) return;

        await _audio.StopAsync();
        FinalizeWav();
        var duration = (DateTime.UtcNow - _recordingStartTime).TotalSeconds;

        State = RecordingState.Transcribing;
        _sounds.PlayStop();
        if (_settings.PauseMediaWhileDictating)
            await _media.ResumeMediaAsync();
        AudioLevel = 0f;

        try
        {
            var text = await _pipeline.RunAsync(
                _activeSession, _currentAudioPath!, duration,
                _settings.AiEnhancementEnabled, _cts!.Token);

            State = RecordingState.Idle;
            if (text is not null)
            {
                LastTranscription = text;

                if (_settings.AutoPasteEnabled)
                {
                    try
                    {
                        await _paste.PasteAtCursorAsync(text, _cts!.Token);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Auto-paste failed");
                    }
                }

                TranscriptionCompleted?.Invoke(this, text);
            }
        }
        catch (OperationCanceledException)
        {
            State = RecordingState.Idle;
        }
        catch (Exception ex)
        {
            State = RecordingState.Idle;
            TranscriptionFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            _activeSession = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static void WriteWavHeader(Stream s)
    {
        // Write a 44-byte WAV header placeholder (16kHz, mono, 16-bit PCM)
        var bw = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
        bw.Write("RIFF"u8);
        bw.Write(0);          // file size - 8 (patched in FinalizeWav)
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);         // chunk size
        bw.Write((short)1);   // PCM format
        bw.Write((short)AudioFormat.Channels);
        bw.Write(AudioFormat.SampleRate);
        bw.Write(AudioFormat.BytesPerSecond);
        bw.Write((short)AudioFormat.BytesPerSample);
        bw.Write((short)AudioFormat.BitsPerSample);
        bw.Write("data"u8);
        bw.Write(0);          // data size (patched in FinalizeWav)
        bw.Flush();
    }

    private void FinalizeWav()
    {
        if (_wavStream is null) return;
        var dataSize = (int)(_wavStream.Length - 44);
        _wavStream.Seek(4, SeekOrigin.Begin);
        var bw = new BinaryWriter(_wavStream, System.Text.Encoding.UTF8, leaveOpen: true);
        bw.Write(dataSize + 36);
        _wavStream.Seek(40, SeekOrigin.Begin);
        bw.Write(dataSize);
        bw.Flush();
        _wavStream.Dispose();
        _wavStream = null;
    }

    public async Task CancelRecordingAsync()
    {
        // If startup is in progress, cancel the CTS so the pipeline setup
        // aborts and the catch block in StartRecordingAsync handles cleanup.
        if (_startingUp)
        {
            _cts?.Cancel();
            return;
        }

        if (State == RecordingState.Idle) return;

        _cts?.Cancel();
        await _audio.StopAsync();
        await (_activeSession?.CancelAsync() ?? Task.CompletedTask);
        _activeSession = null;
        State = RecordingState.Idle;
        _sounds.PlayCancel();
        if (_settings.PauseMediaWhileDictating)
            await _media.ResumeMediaAsync();
        AudioLevel = 0f;
    }

    /// <summary>
    /// Compute RMS of 16-bit PCM samples, normalized to 0.0–1.0.
    /// </summary>
    private static float ComputeRms(byte[] data, int count)
    {
        int sampleCount = count / 2; // 16-bit = 2 bytes per sample
        if (sampleCount == 0) return 0f;

        double sumSquares = 0;
        for (int i = 0; i < count - 1; i += 2)
        {
            short sample = (short)(data[i] | (data[i + 1] << 8));
            sumSquares += sample * (double)sample;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        return (float)Math.Min(rms / short.MaxValue, 1.0);
    }
}
