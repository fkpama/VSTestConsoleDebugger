using Launcher.Settings;
using Microsoft.Diagnostics.Runtime;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.VS;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Sodiware.VisualStudio.Debugger;
using Sodiware.VisualStudio.Logging;

namespace Launcher.Debugger
{
    internal interface IDebugLaunchHost
    {
        Task LaunchAsync(DebugLaunchSettings settings, CancellationToken cancellationToken);
    }
    internal sealed class DebugLauncherHelper : IDisposable
    {
        private readonly IServiceProvider services;
        private readonly ITestAdapterSettings adapterSettings;
        private readonly IDebugLaunchHost host;
        private readonly Lazy<IDebuggerImageTypeService> imageTypeService;
        private readonly IOutputGroupsService outputs;
        private readonly IVsHierarchy projectHier;
        private DebugSession? sessionProcess;
        private readonly IProjectThreadingService threadingService;
        private readonly AsyncLazy<ILogger> _log;
        private readonly AsyncLazy<IVsDebugger> m_debugger;

        private ILogger log => _log.GetValue();

        public DebugLauncherHelper(IServiceProvider services,
                                   ITestAdapterSettings adapterSettings,
                                   IDebugLaunchHost host,
                                   IOutputGroupsService outputs,
                                   IVsHierarchy projectHier,
                                   IProjectThreadingService threadingService,
                                   Lazy<IDebuggerImageTypeService> imageTypeService)
        {
            this.services = services;
            this.adapterSettings = adapterSettings;
            this.host = host;
            this.imageTypeService = imageTypeService;
            this._log = new(adapterSettings.GetLoggerAsync, threadingService.JoinableTaskFactory);
            this.m_debugger = new(async () =>
            {
                await threadingService.SwitchToUIThread();
                return this.services.GetService<SVsShellDebugger, IVsDebugger>();
            }, threadingService.JoinableTaskFactory);
            this.outputs = outputs;
            this.projectHier = projectHier;
            this.threadingService = threadingService;
        }

        public async Task<DebugLaunchSettings?> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
        {
            await Logger.GetOutputWindowPane(Constants.LoggerPaneId)
                .ClearAsync()
                .ConfigureAwait(false);
            var exe = await this.outputs
                .GetKeyOutputAsync(cancellationToken)
                .NoAwait();
            var options = request.Operation;
            var vsTestConsolePath = request.VsTestConsoleExePath.IfMissing(Utils.FindVsTestConsole());

            var targetPath = await getTargetPath(request.Target, cancellationToken).NoAwait();

            var workingDir = request.WorkingDir.IfMissing(Path.GetDirectoryName(targetPath));

            //var env = new Dictionary<string, string>(request.Environment)
            var env = new Dictionary<string, string>();
            request.Environment?.CopyTo(env);
            env[Constants.VsTestEnv.HostDebug] = "1";
            env[Constants.VsTestEnv.NoBreakPoint] = "1";

            var args = new List<string>
            {
                $"\"--TestAdapterPath:{exe}\"",
                "--TestAdapterLoadingStrategy:Explicit,ExtensionsDirectory",
                targetPath
            };

            if (request.AdditionalCommandLine.IsPresent())
                args.Add(request.AdditionalCommandLine!);

            var si = new ProcessStartInfo(vsTestConsolePath)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDir,
                CreateNoWindow = true,
                Arguments = string.Join(" ", args)
            };
            env.CopyTo(si.Environment);

            sessionProcess = new DebugSession(si,
                                              this.threadingService,
                                              this.m_debugger,
                                              this.projectHier,
                                              log);

            var (_, _, settings) = await doStartAsync().NoAwait();

            return settings;

            async Task<(int pid, Guid engine, DebugLaunchSettings settings)> doStartAsync()
            {
                log.LogVerbose($"Loading command \"{vsTestConsolePath}\" {string.Join(" ", args)}");
                var pid = await sessionProcess.StartAsync(cancellationToken).NoAwait();
                var type = getDebuggerType(pid);
                var engine = await DebuggerEngines
                .GetDebugEngineAsync(type, this.imageTypeService)
                .NoAwait();

                var settings = new DebugLaunchSettings(options)
                {
                    Executable = vsTestConsolePath,
                    CurrentDirectory = workingDir,
                    LaunchDebugEngineGuid = engine,
                    ProcessId = pid,
                    LaunchOperation = DebugLaunchOperation.AlreadyRunning,
                    Project = this.projectHier
                };
                env.CopyTo(settings.Environment);
                return (pid, engine, settings);
            }

        }

        private static DebuggerType getDebuggerType(int pid)
        {
            var targets = getTargetFramework(pid);
            if (targets.Length == 0)
            {
                // TODO: Log
                return DebuggerType.ManagedCore;
            }
            return targets[0];
            static DebuggerType[] getTargetFramework(int pid)
            {
                using var dataTarget = DataTarget.AttachToProcess(pid, false);
                var lst = new List<DebuggerType>();
                foreach (var version in dataTarget.ClrVersions)
                {
                    using var rt = version.CreateRuntime();
                    var flavor = rt.ClrInfo.Flavor;
                    switch (flavor)
                    {
                        case ClrFlavor.Desktop:
                            lst.Add(DebuggerType.ManagedOnly);
                            break;
                        case ClrFlavor.Core:
                            lst.Add(DebuggerType.ManagedCore);
                            break;
                        default:
                            // TODO
                            break;
                    }
                }
                return lst.ToArray();
            }
        }

        private async Task<string> getTargetPath(Target entry, CancellationToken cancellationToken)
        {
            string? targetPath;
            if (entry.Mode == ProjectSelectorAction.Project)
            {
                var project = this.services.GetSolution().GetProjectOfGuid(entry.Id!.Value) ?? throw new NotImplementedException();
                targetPath = await project.GetBuildPropertyValueAsync(VSConstantsEx.MSBuildProperties.TargetPath, cancellationToken).NoAwait();
                if (targetPath.IsMissing())
                    throw new NotImplementedException();
            }
            else
            {
                targetPath = entry.TargetPath;
            }

            Assumes.NotNullOrEmpty(targetPath);
            return targetPath;
        }

        public void Dispose()
        {
            this.sessionProcess?.Dispose();
        }

        internal void RegisterPids(uint[] pids)
        {
            Assumes.NotNull(this.sessionProcess);
            this.sessionProcess?.Add(pids);
        }
    }
}
