// VoxScript.Core/Transcription/Core/TranscriptionSegment.cs
namespace VoxScript.Core.Transcription.Core;

/// <summary>
/// A single transcription segment with timestamp boundaries (milliseconds).
/// </summary>
public sealed record TranscriptionSegment(string Text, long StartMs, long EndMs);
