// VoxScript.Native/Parakeet/ParakeetStreamingSession.cs
namespace VoxScript.Native.Parakeet;

/// <summary>
/// Ports the Swift WordAgreementEngine to C#.
/// Maintains a rolling window of overlapping Parakeet decodes and emits
/// "stable" words — words that have appeared consistently across N consecutive
/// overlapping windows at the same position.
/// </summary>
public sealed class WordAgreementEngine
{
    private readonly int _stabilityThreshold;
    private readonly List<string[]> _windowHistory = new();

    // Maps word position -> (word, consecutive agreement count)
    private readonly Dictionary<int, (string Word, int Count)> _stable = new();

    private int _emittedUpTo = -1;

    public WordAgreementEngine(int stabilityThreshold = 3)
    {
        _stabilityThreshold = stabilityThreshold;
    }

    /// <summary>
    /// Feed a new decode window. Returns newly stabilized words since last call.
    /// </summary>
    public IReadOnlyList<string> Feed(string[] words)
    {
        _windowHistory.Add(words);
        if (_windowHistory.Count > _stabilityThreshold * 2)
            _windowHistory.RemoveAt(0);

        // For each position, check agreement across last N windows
        var newStable = new List<string>();
        int maxPos = _windowHistory.Max(w => w.Length);

        for (int pos = _emittedUpTo + 1; pos < maxPos; pos++)
        {
            // Count how many recent windows agree on the word at this position
            int agreement = 0;
            string? candidate = null;

            foreach (var window in _windowHistory.TakeLast(_stabilityThreshold))
            {
                if (pos >= window.Length) break;
                if (candidate is null) { candidate = window[pos]; agreement = 1; }
                else if (window[pos] == candidate) agreement++;
                else { agreement = 0; break; }
            }

            if (agreement >= _stabilityThreshold && candidate is not null)
            {
                newStable.Add(candidate);
                _emittedUpTo = pos;
            }
            else break; // Stop at first unstable position
        }

        return newStable;
    }

    public void Reset()
    {
        _windowHistory.Clear();
        _stable.Clear();
        _emittedUpTo = -1;
    }
}
