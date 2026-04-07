using FluentAssertions;
using VoxScript.Native.Parakeet;
using Xunit;

namespace VoxScript.Tests.Parakeet;

public class MelSpectrogramTests
{
    [Fact]
    public void Compute_returns_correct_dimensions()
    {
        // 1 second of silence at 16kHz
        var samples = new float[16000];
        var result = MelSpectrogram.Compute(samples);

        // n_mels = 80
        result.GetLength(0).Should().Be(80);

        // numFrames = (16000 - 400) / 160 + 1 = 98
        result.GetLength(1).Should().Be(98);
    }

    [Fact]
    public void Compute_silence_produces_low_energy()
    {
        var samples = new float[16000]; // all zeros
        var result = MelSpectrogram.Compute(samples);

        // Log of near-zero energy should be very negative (clamped at log(1e-10) ≈ -23)
        for (int m = 0; m < result.GetLength(0); m++)
            for (int t = 0; t < result.GetLength(1); t++)
                result[m, t].Should().BeLessThan(-20f);
    }

    [Fact]
    public void Compute_sine_wave_produces_nonzero_energy()
    {
        // 440Hz sine wave at 16kHz for 0.5 seconds
        var samples = new float[8000];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = MathF.Sin(2 * MathF.PI * 440f * i / 16000f);

        var result = MelSpectrogram.Compute(samples);

        // Should have 80 mel bins
        result.GetLength(0).Should().Be(80);

        // At least some bins should have significant energy (> -10)
        bool hasEnergy = false;
        for (int m = 0; m < result.GetLength(0); m++)
            for (int t = 0; t < result.GetLength(1); t++)
                if (result[m, t] > -10f) hasEnergy = true;
        hasEnergy.Should().BeTrue("a 440Hz sine should produce energy in some mel bins");
    }

    [Fact]
    public void Compute_short_audio_returns_at_least_one_frame()
    {
        // Very short audio (less than one window)
        var samples = new float[100];
        var result = MelSpectrogram.Compute(samples);

        result.GetLength(0).Should().Be(80);
        result.GetLength(1).Should().BeGreaterThanOrEqualTo(1);
    }
}
