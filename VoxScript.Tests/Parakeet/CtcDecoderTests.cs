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
        // Blank = last index (3). Make blank have highest logit at all timesteps.
        var logits = new DenseTensor<float>(new[] { 1, 3, 4 });
        for (int t = 0; t < 3; t++)
        {
            logits[0, t, 0] = 1f;
            logits[0, t, 1] = 1f;
            logits[0, t, 2] = 1f;
            logits[0, t, 3] = 10f; // blank (last index) wins
        }

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEmpty("all frames are blank");
    }

    [Fact]
    public void Decode_collapses_repeated_tokens()
    {
        // Shape: [1, 5, 4] — 5 time steps, vocab size 4 (blank=3)
        // Token sequence: 0, 0, 0, 1, 1 → should collapse to [0, 1]
        var logits = new DenseTensor<float>(new[] { 1, 5, 4 });
        // Frame 0-2: token 0 wins
        for (int t = 0; t < 3; t++) { logits[0, t, 0] = 10f; logits[0, t, 1] = -10f; logits[0, t, 2] = -10f; logits[0, t, 3] = -10f; }
        // Frame 3-4: token 1 wins
        for (int t = 3; t < 5; t++) { logits[0, t, 0] = -10f; logits[0, t, 1] = 10f; logits[0, t, 2] = -10f; logits[0, t, 3] = -10f; }

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEquivalentTo([0, 1]);
    }

    [Fact]
    public void Decode_handles_blank_between_same_tokens()
    {
        // Shape: [1, 3, 4] — vocab size 4 (blank=3)
        // Token sequence: 1, blank(3), 1 → should produce [1, 1]
        var logits = new DenseTensor<float>(new[] { 1, 3, 4 });
        // Frame 0: token 1
        logits[0, 0, 0] = -10f; logits[0, 0, 1] = 10f; logits[0, 0, 2] = -10f; logits[0, 0, 3] = -10f;
        // Frame 1: blank (token 3)
        logits[0, 1, 0] = -10f; logits[0, 1, 1] = -10f; logits[0, 1, 2] = -10f; logits[0, 1, 3] = 10f;
        // Frame 2: token 1 again
        logits[0, 2, 0] = -10f; logits[0, 2, 1] = 10f; logits[0, 2, 2] = -10f; logits[0, 2, 3] = -10f;

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEquivalentTo([1, 1]);
    }

    [Fact]
    public void Decode_produces_correct_sequence()
    {
        // Shape: [1, 8, 5] — vocab size 5 (blank=4)
        // Token sequence: blank, 2, 2, blank, 0, 1, 1, blank → should produce [2, 0, 1]
        var logits = new DenseTensor<float>(new[] { 1, 8, 5 });
        int[] expected_argmax = [4, 2, 2, 4, 0, 1, 1, 4];
        for (int t = 0; t < 8; t++)
        {
            for (int v = 0; v < 5; v++)
                logits[0, t, v] = -10f;
            logits[0, t, expected_argmax[t]] = 10f;
        }

        var result = ParakeetBackend.GreedyCtcDecode(logits);
        result.Should().BeEquivalentTo([2, 0, 1]);
    }

    [Fact]
    public void Decode_with_explicit_blank_token_zero()
    {
        // Verify explicit blankToken=0 still works (standard CTC convention)
        var logits = new DenseTensor<float>(new[] { 1, 3, 4 });
        for (int t = 0; t < 3; t++)
        {
            logits[0, t, 0] = 10f; // blank (explicit 0) wins
            logits[0, t, 1] = 1f;
            logits[0, t, 2] = 1f;
            logits[0, t, 3] = 1f;
        }

        var result = ParakeetBackend.GreedyCtcDecode(logits, blankToken: 0);
        result.Should().BeEmpty("all frames are blank when blankToken=0");
    }
}
