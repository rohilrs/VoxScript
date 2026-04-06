using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

public sealed class TranscriptionServiceRegistry
{
    private readonly IEnumerable<ITranscriptionService> _services;
    private readonly IEnumerable<IStreamingProvider> _streamingProviders;

    public TranscriptionServiceRegistry(
        IEnumerable<ITranscriptionService> services,
        IEnumerable<IStreamingProvider> streamingProviders)
    {
        _services = services;
        _streamingProviders = streamingProviders;
    }

    public ITranscriptionSession CreateSession(ITranscriptionModel model)
    {
        if (model.SupportsStreaming)
        {
            var provider = _streamingProviders.FirstOrDefault(p => p.Provider == model.Provider)
                ?? throw new InvalidOperationException(
                    $"No streaming provider registered for {model.Provider}");
            return new StreamingTranscriptionSession(model, provider);
        }

        var service = _services.FirstOrDefault(s => s.Provider == model.Provider)
            ?? throw new InvalidOperationException(
                $"No transcription service registered for {model.Provider}");
        return new FileTranscriptionSession(model, service);
    }
}
