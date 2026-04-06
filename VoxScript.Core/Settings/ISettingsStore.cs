namespace VoxScript.Core.Settings;

public interface ISettingsStore
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    bool Contains(string key);
    void Remove(string key);
}
