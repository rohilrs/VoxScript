namespace VoxScript.Core.Audio;

public interface ISoundEffectsService
{
    void PlayStart();
    void PlayToggle();
    void PlayStop();
    void PlayCancel();
}
