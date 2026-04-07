// VoxScript.Native/Parakeet/ParakeetTokenizer.cs
using Microsoft.ML.Tokenizers;

namespace VoxScript.Native.Parakeet;

/// <summary>
/// SentencePiece BPE tokenizer for Parakeet, backed by Microsoft.ML.Tokenizers.
/// Loads a .model file exported from NeMo and decodes token IDs to text.
/// </summary>
public sealed class ParakeetTokenizer : IDisposable
{
    private readonly SentencePieceTokenizer _tokenizer;

    public ParakeetTokenizer(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"SentencePiece model not found: {modelPath}", modelPath);

        using var stream = File.OpenRead(modelPath);
        _tokenizer = SentencePieceTokenizer.Create(
            stream,
            addBeginningOfSentence: false,
            addEndOfSentence: false,
            specialTokens: null);
    }

    public string Decode(List<int> tokenIds)
    {
        if (tokenIds.Count == 0) return string.Empty;
        return _tokenizer.Decode(tokenIds) ?? string.Empty;
    }

    public void Dispose()
    {
        // SentencePieceTokenizer does not implement IDisposable; nothing to dispose.
    }
}
