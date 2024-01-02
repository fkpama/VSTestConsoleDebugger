using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Launcher.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Sodiware.VisualStudio.Logging;

namespace Launcher;

internal static class Log
{
    private static AsyncLazy<ILogger> s_log = new(async() =>
    {
        var services = ServiceProvider.GlobalProvider;
        var settings = services.GetMEFService<ITestAdapterSettings>();
        return await settings.GetLoggerAsync();
    }, ThreadHelper.JoinableTaskFactory);

    internal static ILogger Logger
    {
        get => s_log.GetValue();
    }

    internal static void LogInformation(string message)
        => Logger.LogInformation(message);
}
