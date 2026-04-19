using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace VoxScript.Core.Persistence;

[Index(nameof(CreatedAt))]
public sealed class TranscriptionRecord
{
    public int Id { get; set; }
    [Required] public string Text { get; set; } = string.Empty;
    public string? EnhancedText { get; set; }
    public double DurationSeconds { get; set; }
    public string? ModelName { get; set; }
    public string? Language { get; set; }
    public bool WasAiEnhanced { get; set; }
    public int WordCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
