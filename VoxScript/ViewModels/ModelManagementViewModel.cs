// VoxScript/ViewModels/ModelManagementViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Infrastructure;
using VoxScript.Native.Parakeet;
using VoxScript.Native.Whisper;

namespace VoxScript.ViewModels;

public sealed partial class ModelManagementViewModel : ObservableObject
{
    private readonly WhisperModelManager _modelManager;
    private readonly AppSettings _settings;
    private readonly ILocalTranscriptionBackend _backend;

    public ObservableCollection<ModelDisplayItem> Models { get; } = new();

    [ObservableProperty]
    public partial string? ActiveModelName { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    [ObservableProperty]
    public partial string? DownloadingModelName { get; set; }

    [ObservableProperty]
    public partial string CustomUrl { get; set; } = "";

    [ObservableProperty]
    public partial string CustomName { get; set; } = "";

    public ModelManagementViewModel()
    {
        _modelManager = ServiceLocator.Get<WhisperModelManager>();
        _settings = ServiceLocator.Get<AppSettings>();
        _backend = ServiceLocator.Get<ILocalTranscriptionBackend>();
        ActiveModelName = _settings.SelectedModelName ?? PredefinedModels.Default.Name;
        Refresh();
    }

    public void Refresh()
    {
        Models.Clear();
        var downloaded = _modelManager.ListDownloaded();

        // Predefined models
        foreach (var model in PredefinedModels.All)
        {
            Models.Add(new ModelDisplayItem(
                model.Name,
                model.DisplayName,
                FormatSize(model.FileSizeBytes),
                downloaded.Contains(model.Name),
                model.Name == ActiveModelName,
                IsPredefined: true));
        }

        // Custom models (downloaded but not predefined)
        var predefinedNames = PredefinedModels.All.Select(m => m.Name).ToHashSet();
        foreach (var name in downloaded.Where(n => !predefinedNames.Contains(n)))
        {
            var path = _modelManager.GetModelPath(name);
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            Models.Add(new ModelDisplayItem(
                name,
                name,
                FormatSize(size),
                IsDownloaded: true,
                name == ActiveModelName,
                IsPredefined: false));
        }

        // Also list custom ONNX models (Parakeet)
        var modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxScript", "Models", "whisper");
        if (Directory.Exists(modelsDir))
        {
            var onnxFiles = Directory.GetFiles(modelsDir, "*.onnx");
            foreach (var onnxPath in onnxFiles)
            {
                var name = Path.GetFileNameWithoutExtension(onnxPath);
                if (Models.Any(m => m.Name == name)) continue;
                var size = new FileInfo(onnxPath).Length;
                Models.Add(new ModelDisplayItem(
                    name, name, FormatSize(size),
                    IsDownloaded: true,
                    name == ActiveModelName,
                    IsPredefined: false));
            }
        }
    }

    public async Task UseModelAsync(string modelName)
    {
        var item = Models.FirstOrDefault(m => m.Name == modelName);
        if (item is null) return;

        if (!item.IsDownloaded)
        {
            var predefined = PredefinedModels.All.FirstOrDefault(m => m.Name == modelName);
            if (predefined?.DownloadUrl is null) return;
            await DownloadModelAsync(modelName, predefined.DownloadUrl);
        }

        // Determine which backend to load based on model provider
        var model = PredefinedModels.All.FirstOrDefault(m => m.Name == modelName);
        if (model?.Provider == ModelProvider.Parakeet)
        {
            var parakeetBackend = ServiceLocator.Get<ParakeetBackend>();
            var modelPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoxScript", "Models", "whisper", $"{modelName}.onnx");
            parakeetBackend.UnloadModel();
            await parakeetBackend.LoadModelAsync(modelPath, CancellationToken.None);
        }
        else
        {
            var modelPath = _modelManager.GetModelPath(modelName);
            _backend.UnloadModel();
            await _backend.LoadModelAsync(modelPath, CancellationToken.None);
        }

        ActiveModelName = modelName;
        _settings.SelectedModelName = modelName;
        Refresh();
    }

    public async Task DownloadModelAsync(string name, string url)
    {
        IsDownloading = true;
        DownloadingModelName = name;
        DownloadProgress = 0;

        try
        {
            var predefined = PredefinedModels.All.FirstOrDefault(m => m.Name == name);
            if (predefined?.DownloadUrl is not null)
                await _modelManager.DownloadAsync(name, new Progress<double>(p => DownloadProgress = p), CancellationToken.None);
            else
                await _modelManager.DownloadFromUrlAsync(url, name, new Progress<double>(p => DownloadProgress = p), CancellationToken.None);

            Refresh();
        }
        finally
        {
            IsDownloading = false;
            DownloadingModelName = null;
        }
    }

    public void DeleteModel(string modelName)
    {
        if (modelName == ActiveModelName) return; // Can't delete active model
        _modelManager.DeleteModel(modelName);
        Refresh();
    }

    public async Task ImportLocalFileAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".onnx")
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var destDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoxScript", "Models", "whisper");
            Directory.CreateDirectory(destDir);
            var destOnnx = Path.Combine(destDir, $"{name}.onnx");
            File.Copy(filePath, destOnnx, overwrite: true);

            // Copy co-located .model tokenizer if it exists
            var tokenizerSrc = Path.ChangeExtension(filePath, ".model");
            if (File.Exists(tokenizerSrc))
            {
                var tokenizerDest = Path.Combine(destDir, $"{name}.model");
                File.Copy(tokenizerSrc, tokenizerDest, overwrite: true);
            }

            Refresh();
            await UseModelAsync(name);
        }
        else
        {
            var name = _modelManager.ImportModel(filePath);
            Refresh();
            await UseModelAsync(name);
        }
    }

    public async Task DownloadCustomAsync()
    {
        var url = CustomUrl.Trim();
        var name = CustomName.Trim();
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) return;

        await DownloadModelAsync(name, url);
        CustomUrl = "";
        CustomName = "";
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null or 0) return "";
        var mb = bytes.Value / (1024.0 * 1024.0);
        return mb >= 1024 ? $"{mb / 1024:F1} GB" : $"{mb:F0} MB";
    }
}

public sealed record ModelDisplayItem(
    string Name,
    string DisplayName,
    string SizeText,
    bool IsDownloaded,
    bool IsActive,
    bool IsPredefined);
