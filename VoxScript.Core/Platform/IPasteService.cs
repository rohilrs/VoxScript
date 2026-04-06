namespace VoxScript.Core.Platform;

public interface IPasteService
{
    Task PasteAtCursorAsync(string text, CancellationToken ct);
}
