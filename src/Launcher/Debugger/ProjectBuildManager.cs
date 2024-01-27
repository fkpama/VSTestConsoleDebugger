using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Sodiware.VisualStudio.Events;
using Sodiware.VisualStudio.Logging;

namespace Launcher.Debugger;

partial class DebugLaunchCommandHook
{

    internal sealed class ProjectBuildManager : IDisposable
    {
        private readonly IVsSolution solution;
        private readonly IVsSolutionBuildManager2 buildManager;
        private readonly IVsDebugger debugger;
        private readonly ILogger log;
        private bool disposed;
        private bool isDebugging;
        private readonly CancellationTokenSource cts = new();

        private CancellationToken cancellationToken => cts.Token;
        public event EventHandler? DebuggingStop;
        //private uint updateCookie;
        //private IVsHierarchy[]? targetHierarchies;
        //private LaunchCommand command;

        public string? CommandText
        {
            get
            {
                if (!this.isDebugging) return null;
                var mode = VsUtils.RunOnUIThread(() => this.debugger.GetMode());
                if (mode == DBGMODE.DBGMODE_Break)
                {
                    return "Continue";
                }
                return null;
            }
        }

        public ProjectBuildManager(IVsSolution solution,
                                   IVsSolutionBuildManager2 buildManager,
                                   IVsDebugger debugger,
                                   ILogger log)
        {
            this.solution = solution;
            this.buildManager = buildManager;
            this.debugger = debugger;
            this.log = log;
        }

        internal DebuggingState State
        {
            get;
            private set;
        }
        public bool IsCancelled
        {
            get => this.cts.IsCancellationRequested;
        }

        public void Dispose()
        {
            this.disposed = true;
            this.unregister();
        }

        internal bool Build(IReadOnlyCollection<IMsTestProject> projects, LaunchCommand command)
        {
            checkNotDisposed();
            this.buildManager.QueryBuildManagerBusy(out var busy).RequireOk();
            if (Convert.ToBoolean(busy))
            {
                return false;
            }
            var ar = this.solution.GetStartupProjects()
                .Select(x => x.Hierarchy)
                .ToArray();

            if(!getBuildProjects(projects, ar, out var buildAr))
            {
                return false;
            }

            if (this.IsCancelled)
            {
                return false;
            }
            Assumes.NotNull(buildAr);
            this.State = DebuggingState.Building;
            tryOpenBuildWindow();

            this.State = DebuggingState.Building;
            try
            {
                //((IVsSolutionBuildManager5)this.buildManager)
                //    .AdviseUpdateSolutionEventsAsync(this, out this.updateCookie);
                BuildEvents.BuildEnd += onBuildEnd;
                this.buildManager
                    .StartUpdateProjectConfigurations((uint)buildAr.Length,
                                                      buildAr,
                                                      (int)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD
                                                      ,
                                                      1)
                    .RequireOk();

                if (this.IsCancelled)
                {
                    BuildEvents.BuildEnd -= onBuildEnd;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                // TODO: log
                log.LogError($"Error while building projects: {ex.Message}");
                this.State = DebuggingState.None;
                return false;
            }

            void onBuildEnd(object sender, BuildFinishedEventArgs args)
            {
                if (this.disposed)
                {
                    return;
                }
                BuildEvents.BuildEnd -= onBuildEnd;
                var state = DebuggingState.None;
                try
                {
                    if (!args.Succeeded)
                    {
                        return;
                    }
                    var flags = toCommand(command);
                    VsUtils.RunOnUIThread(() =>
                    {
                        DebuggerEvents.Stop += onDebuggerStop;
                        DebuggerEvents.DebuggerStart += onDebuggerStart;
                        this.buildManager
                            .StartUpdateProjectConfigurations((uint)ar.Length,
                                                              ar,
                                                              (uint)flags,
                                                              1)
                            .RequireOk();
                        state = DebuggingState.Debugging;
                    });
                }
                catch
                {
                    DebuggerEvents.Stop -= onDebuggerStop;
                    throw;
                }
                finally
                {
                    this.State = state;
                }
            }
        }

        private void tryOpenBuildWindow()
        {
            try
            {
                var uiShell = ServiceProvider.GlobalProvider.GetService<SVsUIShell, IVsUIShell>();
                var buildPane = Logger.GetOutputWindowPane(VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid);
                buildPane.Clear();
                buildPane.Activate();
                var flags = __VSFINDTOOLWIN.FTW_fForceCreate
                    | __VSFINDTOOLWIN.FTW_fFindFirst;
                Guid guid = new(ToolWindowGuids.Outputwindow);
                uiShell.FindToolWindow((uint)flags, ref guid, out var frame).RequireOk();
                frame.Show().RequireOk();
                if (Marshal.IsComObject(frame))
                    Marshal.ReleaseComObject(frame);
            }
            catch { }
        }

        private static VSSOLNBUILDUPDATEFLAGS toCommand(LaunchCommand command)
            => command switch
            {
                LaunchCommand.StepIntoNewInstance => VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_LAUNCH,

                LaunchCommand.LaunchDebugTarget
                or LaunchCommand.LaunchProjectSelection
                or LaunchCommand.StepIntoNewInstance
                => VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_LAUNCHDEBUG,

                _ => throw new Exception($"Launch command {command} not supported")
            };

        private bool getBuildProjects(
            IReadOnlyCollection<IMsTestProject> projects,
            IVsHierarchy[] ar,
            [NotNullWhen(true)]out IVsHierarchy[]? buildAr)
        {
            buildAr = null;
            for(var i = 0; i < ar.Length; i++)
            {
                var cur = ar[i];
                var msProj = projects.FirstOrDefault(x => x.Is(project: cur));
                if (msProj?.TargetProject is not null)
                {
                    if (buildAr is null)
                    {
                        buildAr = new IVsHierarchy[ar.Length];
                        if (i > 0) Array.Copy(ar, buildAr, i);
                    }

                    buildAr[i] = msProj.TargetProject;
                }
                else if (buildAr is not null)
                {
                    buildAr[i] = cur;
                }
            }
            return buildAr is not null;
        }

        //int IVsDebuggerEvents.OnModeChange(DBGMODE dbgmodeNew)
        //{
        //    if (this.disposed)
        //    {
        //        this.unregister();
        //        return VSConstants.S_OK;
        //    }
        //    this.mode = dbgmodeNew;
        //    if (dbgmodeNew == DBGMODE.DBGMODE_Design)
        //    {
        //        this.State = DebuggingState.None;
        //        try
        //        {
        //            this.unregister();
        //        }
        //        finally
        //        {
        //            this.DebuggingStop?.Invoke(this);
        //        }
        //    }
        //    return VSConstantsEx.S_OK;
        //}

        private void unregister()
        {
            DebuggerEvents.Stop -= onDebuggerStop;
            //if (!ShellEvents.ShellIsShuttingDown && this.cookie != 0)
            //{
            //    this.debugger.UnadviseDebugEventCallback(this.cookie).RequireOk();
            //}
        }

        private void onDebuggerStop(object sender, EventArgs e)
        {
            this.isDebugging = false;
            this.DebuggingStop?.Invoke(this);
        }

        private void onDebuggerStart(object sender, EventArgs e)
        {
            this.isDebugging = true;
        }

        private void checkNotDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
        }
    }
}