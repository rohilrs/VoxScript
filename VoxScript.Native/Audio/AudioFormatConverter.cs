// VoxScript.Native/Audio/AudioFormatConverter.cs
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoxScript.Native.Audio;

/// <summary>Converts arbitrary NAudio WaveFormat to 16kHz mono Int16 PCM byte arrays.</summary>
public static class AudioFormatConverter
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1);

    /// <summary>
    /// Converts raw bytes from WASAPI (in sourceFormat) to 16kHz mono Int16 PCM.
    /// Returns the converted bytes and the byte count written.
    /// </summary>
    public static byte[] Convert(byte[] input, int inputByteCount, WaveFormat sourceFormat)
    {
        // Wrap input bytes in a readable WaveProvider
        var rawProvider = new RawSourceWaveStream(
            new MemoryStream(input, 0, inputByteCount), sourceFormat);

        ISampleProvider sampleProvider = rawProvider.ToSampleProvider();

        // Stereo → mono
        if (sourceFormat.Channels == 2)
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
        else if (sourceFormat.Channels != 1)
            throw new NotSupportedException($"Unsupported channel count: {sourceFormat.Channels}");

        // Resample to 16kHz if needed
        if (sourceFormat.SampleRate != 16000)
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);

        // Read all converted samples
        var floatBuffer = new float[16000 * 2]; // 2 second max per chunk
        var result = new List<byte>();
        int samplesRead;
        while ((samplesRead = sampleProvider.Read(floatBuffer, 0, floatBuffer.Length)) > 0)
        {
            for (int i = 0; i < samplesRead; i++)
            {
                short s = (short)Math.Clamp(floatBuffer[i] * 32767f, short.MinValue, short.MaxValue);
                result.Add((byte)(s & 0xFF));
                result.Add((byte)(s >> 8));
            }
        }
        return result.ToArray();
    }

    /// <summary>Convert Int16 PCM byte array to float32 samples for whisper.cpp.</summary>
    public static float[] PcmInt16ToFloat32(byte[] pcm, int byteCount)
    {
        int sampleCount = byteCount / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }
        return samples;
    }
}
