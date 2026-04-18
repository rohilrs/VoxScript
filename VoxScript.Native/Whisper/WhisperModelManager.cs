// VoxScript.Native/Whisper/WhisperModelManager.cs
namespace VoxScript.Native.Whisper;

public sealed class WhisperModelManager : IWhisperModelManager
{
    private static readonly Dictionary<string, string> KnownModels = new()
    {
        ["ggml-tiny.en"]   = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin",
        ["ggml-base.en"]   = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin",
        ["ggml-small.en"]  = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin",
        ["ggml-medium.en"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.en.bin",
        ["ggml-large-v3"]  = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
        ["ggml-large-v3-turbo"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin",
    };

    private const string SileroVadUrl =
        "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx";

    private readonly string _modelsDir;
    private readonly HttpClient _http;

    public WhisperModelManager(string modelsDir, HttpClient http)
    {
        _modelsDir = modelsDir;
        _http = http;
        Directory.CreateDirectory(modelsDir);
    }

    public string GetModelPath(string modelName) =>
        Path.Combine(_modelsDir, $"{modelName}.bin");

    public string VadModelPath => Path.Combine(_modelsDir, "silero-vad.onnx");
    public bool IsVadDownloaded => File.Exists(VadModelPath);

    public bool IsDownloaded(string modelName) =>
        File.Exists(GetModelPath(modelName));

    public IReadOnlyList<string> ListDownloaded() =>
        Directory.GetFiles(_modelsDir, "*.bin")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Cast<string>()
            .ToList();

    public async Task DownloadAsync(string modelName, IProgress<double>? progress,
        CancellationToken ct)
    {
        if (!KnownModels.TryGetValue(modelName, out var url))
            throw new ArgumentException($"Unknown model: {modelName}");

        var dest = GetModelPath(modelName);
        var tmp = dest + ".tmp";

        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using (var src = await response.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    if (total > 0) progress?.Report((double)downloaded / total);
                }
            } // dst is closed here before the move

            File.Move(tmp, dest, overwrite: true);
        }
        catch
        {
            // On cancel or any error, clean up the partial .tmp so it doesn't linger
            // in the models folder forever. Best-effort — swallow IO errors during cleanup.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    public void DeleteModel(string modelName)
    {
        var path = GetModelPath(modelName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public string ImportModel(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var dest = GetModelPath(name);
        File.Copy(sourcePath, dest, overwrite: true);
        return name;
    }

    public async Task DownloadFromUrlAsync(string url, string name,
        IProgress<double>? progress, CancellationToken ct)
    {
        var dest = GetModelPath(name);
        var tmp = dest + ".tmp";

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0) progress?.Report((double)downloaded / total);
            }
        }

        File.Move(tmp, dest, overwrite: true);
    }

    /// <summary>Download the Silero VAD ONNX model (~2MB).</summary>
    public async Task DownloadVadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var dest = VadModelPath;
        if (File.Exists(dest)) return;

        var tmp = dest + ".tmp";

        using var response = await _http.GetAsync(SileroVadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0) progress?.Report((double)downloaded / total);
            }
        }

        File.Move(tmp, dest, overwrite: true);
    }
}
