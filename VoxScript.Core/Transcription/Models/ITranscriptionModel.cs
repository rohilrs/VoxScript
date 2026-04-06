namespace VoxScript.Core.Transcription.Models;

public interface ITranscriptionModel
{
    ModelProvider Provider { get; }
    string Name { get; }
    string DisplayName { get; }
    bool SupportsStreaming { get; }
    bool IsLocal { get; }
}
