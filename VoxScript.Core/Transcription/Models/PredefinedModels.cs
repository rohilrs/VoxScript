namespace VoxScript.Core.Transcription.Models;

public static class PredefinedModels
{
    public static readonly TranscriptionModel TinyEn = new(
        ModelProvider.Local, "ggml-tiny.en", "Whisper Tiny (English)", false, true,
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin",
        75_000_000L);

    public static readonly TranscriptionModel BaseEn = new(
        ModelProvider.Local, "ggml-base.en", "Whisper Base (English)", false, true,
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin",
        142_000_000L);

    public static readonly TranscriptionModel SmallEn = new(
        ModelProvider.Local, "ggml-small.en", "Whisper Small (English)", false, true,
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin",
        466_000_000L);

    public static readonly TranscriptionModel LargeV3Turbo = new(
        ModelProvider.Local, "ggml-large-v3-turbo", "Whisper Large v3 Turbo", false, true,
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin",
        809_000_000L);

    public static readonly TranscriptionModel ParakeetCtc = new(
        ModelProvider.Parakeet, "parakeet-ctc-0.6b", "Parakeet CTC 0.6B",
        false, true, null, 1_200_000_000L);

    public static readonly IReadOnlyList<TranscriptionModel> All =
        [TinyEn, BaseEn, SmallEn, LargeV3Turbo, ParakeetCtc];

    // Base.en is the sweet spot for CPU-only transcription (fast + decent quality).
    // Switch to LargeV3Turbo once GPU (DirectML/CUDA) is enabled.
    public static readonly TranscriptionModel Default = BaseEn;
}
