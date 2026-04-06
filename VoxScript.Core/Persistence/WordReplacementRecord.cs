namespace VoxScript.Core.Persistence;

public sealed class WordReplacementRecord
{
    public int Id { get; set; }
    public string Original { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
}
