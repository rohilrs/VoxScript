// VoxScript/Infrastructure/AppLogger.cs
using Serilog;
using Serilog.Events;

namespace VoxScript.Infrastructure;

public static class AppLogger
{
    public static void Initialize()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxScript", "Logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.File(
                path: Path.Combine(logDir, "voxscript-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("VoxScript starting. Version: {Version}",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
    }

    public static void Shutdown() => Log.CloseAndFlush();
}
