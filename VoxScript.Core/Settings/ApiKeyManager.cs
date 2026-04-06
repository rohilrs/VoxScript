namespace VoxScript.Core.Settings;

public interface IApiKeyStore
{
    void StoreKey(string service, string key);
    string? GetKey(string service);
    void DeleteKey(string service);
}

public sealed class ApiKeyManager
{
    private readonly IApiKeyStore _store;

    public ApiKeyManager(IApiKeyStore store) => _store = store;

    public void SetOpenAiKey(string key)     => _store.StoreKey("OpenAI", key);
    public string? GetOpenAiKey()            => _store.GetKey("OpenAI");
    public void SetAnthropicKey(string key)  => _store.StoreKey("Anthropic", key);
    public string? GetAnthropicKey()         => _store.GetKey("Anthropic");
    public void SetDeepgramKey(string key)   => _store.StoreKey("Deepgram", key);
    public string? GetDeepgramKey()          => _store.GetKey("Deepgram");
    public void SetElevenLabsKey(string key) => _store.StoreKey("ElevenLabs", key);
    public string? GetElevenLabsKey()        => _store.GetKey("ElevenLabs");
}
