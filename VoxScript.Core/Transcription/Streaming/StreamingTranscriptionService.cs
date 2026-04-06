// VoxScript.Core/Transcription/Streaming/StreamingTranscriptionService.cs
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Streaming;

/// <summary>
/// Resolves the correct IStreamingProvider for a given model and creates
/// a StreamingTranscriptionSession for the pipeline to use.
/// </summary>
public sealed class StreamingTranscriptionService
{
    private readonly IEnumerable<IStreamingProvider> _providers;

    public StreamingTranscriptionService(IEnumerable<IStreamingProvider> providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// Returns the streaming provider matching the model's provider enum, or null if none registered.
    /// </summary>
    public IStreamingProvider? GetProvider(ModelProvider provider) =>
        _providers.FirstOrDefault(p => p.Provider == provider);

    /// <summary>
    /// Creates a streaming transcription session for the given model.
    /// Throws if no provider is registered for the model's provider.
    /// </summary>
    public ITranscriptionSession CreateSession(ITranscriptionModel model)
    {
        var provider = GetProvider(model.Provider)
            ?? throw new InvalidOperationException(
                $"No streaming provider registered for {model.Provider}.");

        return new StreamingTranscriptionSession(model, provider);
    }
}
