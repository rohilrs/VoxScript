// VoxScript.Native/Parakeet/ParakeetModelManager.cs
namespace VoxScript.Native.Parakeet;

public sealed class ParakeetModelManager
{
    private static readonly Dictionary<string, string> KnownModels = new()
    {
        ["parakeet-tdt-0.6b-v2"] =
            "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2/resolve/main/parakeet-tdt-0.6b-v2.onnx",
    };

    private readonly string _modelsDir;
    private readonly HttpClient _http;

    public ParakeetModelManager(string modelsDir, HttpClient http)
    {
        _modelsDir = modelsDir;
        _http = http;
        Directory.CreateDirectory(modelsDir);
    }

    public string GetModelPath(string modelName) =>
        Path.Combine(_modelsDir, $"{modelName}.onnx");

    public bool IsDownloaded(string modelName) =>
        File.Exists(GetModelPath(modelName));

    public async Task DownloadAsync(string modelName, IProgress<double>? progress,
        CancellationToken ct)
    {
        if (!KnownModels.TryGetValue(modelName, out var url))
            throw new ArgumentException($"Unknown Parakeet model: {modelName}");

        var dest = GetModelPath(modelName);
        var tmp = dest + ".tmp";

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(tmp);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0) progress?.Report((double)downloaded / total);
        }

        File.Move(tmp, dest, overwrite: true);
    }
}
