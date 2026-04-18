namespace VoxScript.Core.AI;

public interface IStructuralFormattingService
{
    bool IsConfigured { get; }
    Task<string?> FormatAsync(string text, CancellationToken ct);
}
