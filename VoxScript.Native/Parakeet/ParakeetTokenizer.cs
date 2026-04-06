// VoxScript.Native/Parakeet/ParakeetTokenizer.cs
namespace VoxScript.Native.Parakeet;

/// <summary>
/// Wraps the SentencePiece BPE tokenizer for Parakeet.
/// Full implementation requires the sentencepiece C# binding or a port.
/// Stub decodes token IDs using the vocabulary file.
/// </summary>
public sealed class ParakeetTokenizer
{
    private readonly string[] _vocab;

    public ParakeetTokenizer(string modelPath)
    {
        // TODO: Load SentencePiece model properly.
        // For now, read the vocabulary from a co-located vocab.txt if present.
        var vocabPath = Path.ChangeExtension(modelPath, ".vocab.txt");
        _vocab = File.Exists(vocabPath)
            ? File.ReadAllLines(vocabPath)
            : [];
    }

    public string Decode(List<int> tokenIds)
    {
        if (_vocab.Length == 0) return $"[{tokenIds.Count} tokens]";

        return string.Concat(tokenIds
            .Where(t => t < _vocab.Length)
            .Select(t => _vocab[t].Replace("\u2581", " ")))
            .Trim();
    }
}
