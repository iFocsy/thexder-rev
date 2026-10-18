using Serilog;
using Thexder.Utils;

namespace Thexder.Services
{
    internal static class LoggerService
    {
        // One file per day under data\logs, kept for a week.
        public static void Configure()
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(AppPaths.LogDirectory, "thexder-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }
}
