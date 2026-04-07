using FluentAssertions;
using Microsoft.ML.OnnxRuntime.Tensors;
using VoxScript.Native.Parakeet;
using Xunit;

namespace VoxScript.Tests.Parakeet;

public class CtcDecoderTests
{
    [Fact]
    public void Decode_skips_blank_token()
    {
        // Shape: [1, 3, 4] — 3 time steps, vocab size 4
        // Token 0 is blank. Make blank have highest logit at all timesteps.
        var logits = new DenseTensor<float>(new[] { 1, 3, 4 });
        for (int t = 0; t < 3; t++)
        {
            logits[0, t, 0] = 10f; // blank wins
            logits[0, t, 1] = 1f;
            logits[0, t, 2] = 1f;
            logits[0, t, 3] = 1f;
        }

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEmpty("all frames are blank");
    }

    [Fact]
    public void Decode_collapses_repeated_tokens()
    {
        // Shape: [1, 5, 3] — 5 time steps, vocab size 3
        // Token sequence: 1, 1, 1, 2, 2 → should collapse to [1, 2]
        var logits = new DenseTensor<float>(new[] { 1, 5, 3 });
        // Frame 0-2: token 1 wins
        for (int t = 0; t < 3; t++) { logits[0, t, 0] = -10f; logits[0, t, 1] = 10f; logits[0, t, 2] = -10f; }
        // Frame 3-4: token 2 wins
        for (int t = 3; t < 5; t++) { logits[0, t, 0] = -10f; logits[0, t, 1] = -10f; logits[0, t, 2] = 10f; }

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void Decode_handles_blank_between_same_tokens()
    {
        // Token sequence: 1, blank, 1 → should produce [1, 1] (blank resets repeat suppression)
        var logits = new DenseTensor<float>(new[] { 1, 3, 3 });
        // Frame 0: token 1
        logits[0, 0, 0] = -10f; logits[0, 0, 1] = 10f; logits[0, 0, 2] = -10f;
        // Frame 1: blank (token 0)
        logits[0, 1, 0] = 10f; logits[0, 1, 1] = -10f; logits[0, 1, 2] = -10f;
        // Frame 2: token 1 again
        logits[0, 2, 0] = -10f; logits[0, 2, 1] = 10f; logits[0, 2, 2] = -10f;

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEquivalentTo([1, 1]);
    }

    [Fact]
    public void Decode_produces_correct_sequence()
    {
        // Token sequence: blank, 3, 3, blank, 1, 2, 2, blank → should produce [3, 1, 2]
        var logits = new DenseTensor<float>(new[] { 1, 8, 5 });
        int[] expected_argmax = [0, 3, 3, 0, 1, 2, 2, 0];
        for (int t = 0; t < 8; t++)
        {
            for (int v = 0; v < 5; v++)
                logits[0, t, v] = -10f;
            logits[0, t, expected_argmax[t]] = 10f;
        }

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEquivalentTo([3, 1, 2]);
    }
}
