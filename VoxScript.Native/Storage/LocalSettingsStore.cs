using System.Text.Json;
using VoxScript.Core.Settings;

namespace VoxScript.Native.Storage;

/// <summary>
/// ISettingsStore backed by a local JSON file.
/// Used for unpackaged WinUI apps where ApplicationData.Current is unavailable.
/// Thread-safe: reads/writes are serialized through a lock.
/// </summary>
public sealed class LocalSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private Dictionary<string, JsonElement> _cache;

    public LocalSettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxScript");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        _cache = Load();
    }

    public T? Get<T>(string key)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var element))
                return default;

            try
            {
                return element.Deserialize<T>();
            }
            catch
            {
                return default;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            if (value is null)
            {
                _cache.Remove(key);
            }
            else
            {
                // Round-trip through JSON to store as JsonElement
                var json = JsonSerializer.SerializeToElement(value);
                _cache[key] = json;
            }
            Save();
        }
    }

    public bool Contains(string key)
    {
        lock (_lock)
            return _cache.ContainsKey(key);
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            if (_cache.Remove(key))
                Save();
        }
    }

    private Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                    ?? new Dictionary<string, JsonElement>();
            }
        }
        catch
        {
            // Corrupted file -- start fresh
        }
        return new Dictionary<string, JsonElement>();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
