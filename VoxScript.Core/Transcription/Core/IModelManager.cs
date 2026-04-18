namespace VoxScript.Core.Transcription.Core;

public interface IModelManager
{
    bool IsDownloaded(string modelName);
    bool IsDownloading(string modelName);
}
