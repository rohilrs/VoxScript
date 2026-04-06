// VoxScript.Tests/Audio/AudioFormatConverterTests.cs
using FluentAssertions;
using NAudio.Wave;
using VoxScript.Native.Audio;
using Xunit;

namespace VoxScript.Tests.Audio;

public class AudioFormatConverterTests
{
    [Fact]
    public void PcmInt16ToFloat32_converts_max_positive()
    {
        byte[] pcm = [0xFF, 0x7F]; // 32767 as little-endian Int16
        var samples = AudioFormatConverter.PcmInt16ToFloat32(pcm, 2);
        samples.Should().HaveCount(1);
        samples[0].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void PcmInt16ToFloat32_converts_zero()
    {
        byte[] pcm = [0x00, 0x00];
        var samples = AudioFormatConverter.PcmInt16ToFloat32(pcm, 2);
        samples[0].Should().Be(0f);
    }

    [Fact]
    public void PcmInt16ToFloat32_converts_negative()
    {
        byte[] pcm = [0x00, 0x80]; // -32768 as little-endian Int16
        var samples = AudioFormatConverter.PcmInt16ToFloat32(pcm, 2);
        samples[0].Should().BeApproximately(-1.0f, 0.001f);
    }

    [Fact]
    public void Convert_stereo_44100_to_mono_16000_returns_bytes()
    {
        // Generate 0.1s of silence at 44100Hz stereo Int16
        var srcFormat = new WaveFormat(44100, 16, 2);
        int bytesPerSample = 4; // 2 ch * 2 bytes
        int totalBytes = (int)(44100 * 0.1 * bytesPerSample);
        var input = new byte[totalBytes]; // silence

        var result = AudioFormatConverter.Convert(input, totalBytes, srcFormat);

        // 0.1s at 16kHz mono Int16 = 1600 samples * 2 bytes = 3200 bytes (approx)
        result.Length.Should().BeInRange(3000, 3400);
    }
}
