using Windows.Media.Control;
using VoxScript.Core.Platform;
using Serilog;

namespace VoxScript.Native.Platform;

public sealed class MediaControlService : IMediaControlService
{
    private GlobalSystemMediaTransportControlsSession? _pausedSession;

    public async Task PauseMediaAsync()
    {
        if (_pausedSession is not null) return;

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session is null)
            {
                Log.Debug("No active media session — skipping pause");
                return;
            }

            var status = session.GetPlaybackInfo().PlaybackStatus;
            if (status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                Log.Debug("Media not playing (status={Status}) — skipping pause", status);
                return;
            }

            var ok = await session.TryPauseAsync();
            if (ok)
            {
                _pausedSession = session;
                Log.Debug("Paused media session {AppId}", session.SourceAppUserModelId);
            }
            else
            {
                Log.Debug("TryPauseAsync returned false for {AppId}", session.SourceAppUserModelId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to pause media via SMTC");
        }
    }

    public async Task ResumeMediaAsync()
    {
        var session = _pausedSession;
        if (session is null) return;
        _pausedSession = null;

        try
        {
            var ok = await session.TryPlayAsync();
            if (!ok)
                Log.Debug("TryPlayAsync returned false for {AppId}", session.SourceAppUserModelId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resume media via SMTC");
        }
    }
}
