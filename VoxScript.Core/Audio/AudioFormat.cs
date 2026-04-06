namespace VoxScript.Core.Audio;

public static class AudioFormat
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;
    public const int BytesPerSample = BitsPerSample / 8;
    public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;
}
