namespace VoxScript.Tests.Helpers;

public static class WavTestHelper
{
    /// <summary>Creates a minimal valid 16kHz mono Int16 PCM WAV byte array.</summary>
    public static byte[] CreateSilenceWav(double durationSeconds)
    {
        int sampleRate = 16000;
        int numSamples = (int)(sampleRate * durationSeconds);
        int dataSize = numSamples * 2;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);  // PCM
        writer.Write((short)1);  // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // byte rate
        writer.Write((short)2);  // block align
        writer.Write((short)16); // bits per sample
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]); // silence

        return ms.ToArray();
    }
}
