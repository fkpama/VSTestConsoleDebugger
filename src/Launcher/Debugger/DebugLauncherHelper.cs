using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Launcher.Settings;
using Microsoft.Diagnostics.Runtime;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Sodiware.VisualStudio.Logging;
using static Microsoft.VisualStudio.VSConstants;

namespace Launcher.Debugger
{
    internal sealed class DebugLauncherHelper : IDisposable
    {
        private readonly IServiceProvider services;
        private readonly ITestAdapterSettings adapterSettings;
        private readonly Lazy<IDebuggerImageTypeService> imageTypeService;
        private readonly IOutputGroupsService outputs;
        private readonly IVsHierarchy projectHier;
        private DebugSession? sessionProcess;
        private readonly IProjectThreadingService threadingService;
        private readonly AsyncLazy<ILogger> _log;

        private ILogger log => _log.GetValue();

        public DebugLauncherHelper(IServiceProvider services,
                                   ITestAdapterSettings adapterSettings,
                                   IOutputGroupsService outputs,
                                   IVsHierarchy projectHier,
                                   IProjectThreadingService threadingService,
                                   Lazy<IDebuggerImageTypeService> imageTypeService)
        {
            this.services = services;
            this.adapterSettings = adapterSettings;
            this.imageTypeService = imageTypeService;
            this._log = new(adapterSettings.GetLoggerAsync, threadingService.JoinableTaskFactory);
            this.outputs = outputs;
            this.projectHier = projectHier;
            this.threadingService = threadingService;
        }

        public async Task<DebugLaunchSettings> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken)
        {
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

            sessionProcess = new DebugSession(si, log);

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

            return settings;
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
                targetPath = await project.GetBuildPropertyValueAsync(VSConstantsEx.MSBuildProperties.TargetPath).NoAwait();
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

        internal async Task BuildAsync(Target target)
        {
            if (target.Id is null)
                return;
            if (!this.threadingService.IsOnMainThread)
                await this.threadingService.SwitchToUIThread();

            var solution = this.services.GetSolution();
            var project = (IVsProject?)solution.GetProjectOfGuid(target.Id.Value);
            if (project is null)
            {
                throw new NotImplementedException();
            }

            var cfg = project.GetActiveCfg() as IVsProjectCfg;
            if (cfg is null)
            {
                throw new NotImplementedException();
            }
            ErrorHandler.ThrowOnFailure(cfg.get_BuildableProjectCfg(out var b));

            var buildManager = this.services.GetService<SVsBuildManagerAccessor, IVsBuildManagerAccessor4>();
            var available = buildManager.UIThreadIsAvailableForBuild;
            var hr = buildManager.AcquireBuildResources(VSBUILDMANAGERRESOURCE.VSBUILDMANAGERRESOURCE_UITHREAD, out var cookie);
            ErrorHandler.ThrowOnFailure(hr);
            try
            {
                var b1 = buildManager.SolutionBuildAvailable.ToInt32();
                //hr = buildManager.ClaimUIThreadForBuild();
                //ErrorHandler.ThrowOnFailure(hr);
                var tcs = new TaskCompletionSource();
                void handler(object o, ProjectBuildEndEventArgs e)
                {
                    ProjectEvents.OnBuildEnd -= handler;
                    tcs.SetAsyncCompleted();
                };
                ProjectEvents.OnBuildEnd += handler;
                var pane = Logger.GetOutputWindowPane(OutputWindowPaneGuid.BuildOutputPane_guid);
                ErrorHandler.ThrowOnFailure(b.StartBuild(pane, 0));
                await tcs.Task.WithTimeout(TimeSpan.FromSeconds(5));
                //hr = buildManager.ReleaseUIThreadForBuild();
            }
            finally
            {
                if (cookie != 0)
                {
                    buildManager.ReleaseBuildResources(cookie);
                }
            }
            await TaskScheduler.Default;
        }

        internal void RegisterPids(uint[] pids)
        {
        }
    }
}
