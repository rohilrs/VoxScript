namespace VoxScript.Core.Platform;

public interface IMediaControlService
{
    Task PauseMediaAsync();
    Task ResumeMediaAsync();
}
