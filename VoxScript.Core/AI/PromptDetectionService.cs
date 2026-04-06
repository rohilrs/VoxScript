namespace VoxScript.Core.AI;

/// <summary>Detects structured prompt commands embedded in dictated text.</summary>
public sealed class PromptDetectionService
{
    private static readonly string[] PromptTriggers =
        ["format this as", "make this a", "clean up", "translate to", "summarize"];

    public (string cleanText, string? detectedInstruction) Analyze(string text)
    {
        foreach (var trigger in PromptTriggers)
        {
            int idx = text.IndexOf(trigger, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var instruction = text[idx..].Trim();
                var cleanText = text[..idx].Trim();
                return (cleanText, instruction);
            }
        }
        return (text, null);
    }
}
