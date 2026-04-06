namespace VoxScript.Core.Persistence;

public sealed class CorrectionRecord
{
    public int Id { get; set; }
    public string Wrong { get; set; } = string.Empty;
    public string Correct { get; set; } = string.Empty;
}
