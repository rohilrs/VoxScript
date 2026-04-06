namespace VoxScript.Core.Transcription.Models;

public enum ModelProvider
{
    Local,       // whisper.cpp
    Parakeet,    // ONNX Runtime
    OpenAI,
    Deepgram,
    ElevenLabs,
    Soniox,
    OpenAICompatible
}
