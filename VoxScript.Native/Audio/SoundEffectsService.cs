using System.Media;
using VoxScript.Core.Audio;
using VoxScript.Core.Settings;
using Serilog;

namespace VoxScript.Native.Audio;

public sealed class SoundEffectsService : ISoundEffectsService
{
    private readonly AppSettings _settings;
    private readonly SoundPlayer? _startPlayer;
    private readonly SoundPlayer? _togglePlayer;
    private readonly SoundPlayer? _stopPlayer;

    public SoundEffectsService(AppSettings settings)
    {
        _settings = settings;

        var baseDir = AppContext.BaseDirectory;
        _startPlayer = LoadPlayer(Path.Combine(baseDir, "Assets", "Sounds", "start.wav"));
        _togglePlayer = LoadPlayer(Path.Combine(baseDir, "Assets", "Sounds", "toggle.wav"));
        _stopPlayer = LoadPlayer(Path.Combine(baseDir, "Assets", "Sounds", "stop.wav"));
    }

    public void PlayStart()
    {
        if (_settings.SoundEffectsEnabled)
            _startPlayer?.Play();
    }

    public void PlayToggle()
    {
        if (_settings.SoundEffectsEnabled)
            _togglePlayer?.Play();
    }

    public void PlayStop()
    {
        if (_settings.SoundEffectsEnabled)
            _stopPlayer?.Play();
    }

    private static SoundPlayer? LoadPlayer(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Log.Warning("Sound file not found: {Path}", path);
                return null;
            }
            var player = new SoundPlayer(path);
            player.Load();
            return player;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load sound: {Path}", path);
            return null;
        }
    }
}
