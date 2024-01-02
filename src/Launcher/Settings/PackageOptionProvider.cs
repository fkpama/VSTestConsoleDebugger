using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Sodiware.VisualStudio.Logging;

namespace Launcher.Settings
{
    [Export(typeof(ITestAdapterSettings))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal sealed class PackageOptionProvider : ITestAdapterSettings
    {
        readonly IServiceProvider services;
        Package? package;
        [ImportingConstructor]
        public PackageOptionProvider(SVsServiceProvider services)
        {
            this.services = services;
        }

        private async Task<Package> getPackage(CancellationToken cancellationToken = default)
        {
            if (this.package is not null)
                return this.package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var shell = this.services.GetService<SVsShell, IVsShell>();
            var packageId  = Constants.PackageId;
            shell.LoadPackage(ref packageId, out var package).RequireOk();
            this.package = (Package)package;
            return this.package;
        }

        public async Task<LogLevel> GetLogLevelAsync(CancellationToken cancellationToken)
        {
            var pkg = await getPackage(cancellationToken);
            var page = (MsTestAdapterDebuggerOptionPage)pkg.GetDialogPage(typeof(MsTestAdapterDebuggerOptionPage));
            return page.LogLevel;
        }

        public async Task<ILogger> GetLoggerAsync()
        {
            var cancellation = CancellationToken.None;
            var pkgTask = this.getPackage();
            var logger = Logger.OutputWindow(Constants.LoggerPaneId, Resources.OutputPaneTitle);
            var pkg = await pkgTask.ConfigureAwait(false);
            return logger;
        }
    }
}
