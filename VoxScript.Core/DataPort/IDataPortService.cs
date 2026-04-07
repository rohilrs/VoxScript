namespace VoxScript.Core.DataPort;

public interface IDataPortService
{
    Task<ExportResult> ExportAsync(Stream output, CancellationToken ct);
    Task<ImportResult> ImportAsync(Stream input, CancellationToken ct);
}
