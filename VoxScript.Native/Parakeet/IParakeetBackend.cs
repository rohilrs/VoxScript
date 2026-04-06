// VoxScript.Native/Parakeet/IParakeetBackend.cs
namespace VoxScript.Native.Parakeet;

public interface IParakeetBackend
{
    bool IsModelLoaded { get; }
    Task LoadModelAsync(string modelPath, CancellationToken ct);
    /// <summary>
    /// Transcribe 16kHz mono float32 PCM samples using Parakeet TDT ONNX model.
    /// Returns word-level tokens for use in WordAgreementEngine.
    /// </summary>
    Task<ParakeetResult> TranscribeAsync(float[] samples, CancellationToken ct);
}

public sealed record ParakeetResult(string Text, IReadOnlyList<WordToken> Words);

public sealed record WordToken(string Word, double StartSec, double EndSec, float Confidence);
