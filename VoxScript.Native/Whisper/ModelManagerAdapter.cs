using VoxScript.Core.Transcription.Core;

namespace VoxScript.Native.Whisper;

public sealed class ModelManagerAdapter : IModelManager
{
    private readonly WhisperModelManager _manager;

    public ModelManagerAdapter(WhisperModelManager manager) => _manager = manager;

    public bool IsDownloaded(string modelName) => _manager.IsDownloaded(modelName);

    public bool IsDownloading(string modelName) => false;
}
