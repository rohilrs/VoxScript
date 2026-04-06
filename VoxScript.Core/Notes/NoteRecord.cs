using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace VoxScript.Core.Notes;

[Index(nameof(ModifiedAt))]
public sealed class NoteRecord
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string ContentRtf { get; set; } = string.Empty;

    public string ContentPlainText { get; set; } = string.Empty;

    public bool IsStarred { get; set; }

    public int? SourceTranscriptionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
