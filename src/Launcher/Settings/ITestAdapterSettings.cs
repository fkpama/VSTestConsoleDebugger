using Sodiware.VisualStudio.Logging;

namespace Launcher.Settings
{
    public interface ITestAdapterSettings
    {
        Task<ILogger> GetLoggerAsync();
        Task<LogLevel> GetLogLevelAsync(CancellationToken cancellationToken);
    }
}
