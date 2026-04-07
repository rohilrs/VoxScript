using System.Text.Json.Serialization;

namespace VoxScript.Core.DataPort;

public sealed class DataPortPayload
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; set; }

    [JsonPropertyName("vocabulary")]
    public List<string> Vocabulary { get; set; } = [];

    [JsonPropertyName("corrections")]
    public List<CorrectionDto> Corrections { get; set; } = [];

    [JsonPropertyName("expansions")]
    public List<ExpansionDto> Expansions { get; set; } = [];
}

public sealed class CorrectionDto
{
    [JsonPropertyName("wrong")]
    public string Wrong { get; set; } = string.Empty;

    [JsonPropertyName("correct")]
    public string Correct { get; set; } = string.Empty;
}

public sealed class ExpansionDto
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = string.Empty;

    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }
}

public sealed class ExportResult
{
    public int VocabularyCount { get; init; }
    public int CorrectionsCount { get; init; }
    public int ExpansionsCount { get; init; }
}

public sealed class ImportResult
{
    public int VocabularyAdded { get; init; }
    public int CorrectionsAdded { get; init; }
    public int ExpansionsAdded { get; init; }
    public int Skipped { get; init; }
}
