using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using OLEConstants = Microsoft.VisualStudio.OLE.Interop.Constants;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using IServiceProvider = System.IServiceProvider;
using Sodiware.VisualStudio.Events;
using Sodiware.VisualStudio.Logging;
using Microsoft.VisualStudio.Shell.Events;

namespace Launcher.Debugger
{
    internal interface IDebugLaunchCommandHook : IOleCommandTarget
    {
        void Remove(IMsTestProject msTestProject);
        void Add(IMsTestProject msTestProject);
    }

    [Export(typeof(IDebugLaunchCommandHook))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    sealed partial class DebugLaunchCommandHook
        : IDebugLaunchCommandHook,
        IDisposable
    {

        internal enum LaunchCommand
        {
            None,
            LaunchDebugTarget,
            LaunchProjectSelection,
            StartWithoutDebugging,
            StepIntoNewInstance
        }
        internal enum DebuggingState
        {
            None      = 0,
            Building  = 1,
            Debugging = 2,
            Error     = 3
        }
        private uint cookie;
        private List<IMsTestProject>? startupProject;
        private readonly IServiceProvider serviceProvider;
        private readonly Lazy<IVsSolution> solution;
        private readonly Lazy<IVsDebugger> debugger;
        private readonly ILogger log;
        private readonly List<IMsTestProject> projects = new();
        private ProjectBuildManager? buildManager;
        private DebuggingState DebugState
            => buildManager?.State ?? DebuggingState.None;

        internal bool IsDebugging => DebugState == DebuggingState.Debugging;
        internal bool IsBuilding => DebugState == DebuggingState.Building;
        internal bool CanDebug => this.buildManager is null && !IsDebugging && !IsBuilding;

        internal IVsRegisterPriorityCommandTarget RegisterPriorityCommandTarget
            => this.serviceProvider.GetService<SVsRegisterPriorityCommandTarget, IVsRegisterPriorityCommandTarget>();

        internal IVsSolutionBuildManager SolutionBuildManager
            => this.serviceProvider.GetService<SVsSolutionBuildManager, IVsSolutionBuildManager>();

        internal IVsSolution Solution => this.solution.Value;

        public bool IsEnabled => this.cookie != 0;

        [ImportingConstructor]
        public DebugLaunchCommandHook(
            [Import(typeof(SVsServiceProvider))]
            IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            this.solution = serviceProvider.GetLazyService<SVsSolution, IVsSolution>();
            this.debugger = serviceProvider.GetLazyService<SVsShellDebugger, IVsDebugger>();
            this.log = Log.Logger;
        }

        int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup,
                                          uint cCmds,
                                          OLECMD[] prgCmds,
                                          IntPtr pCmdText)
        {
            if (this.IsEnabled)
            {
                for (var i = 0; i < prgCmds.Length; i++)
                {
                    ref var cmd = ref prgCmds[i];
                    if (isDebugLaunchCommand(pguidCmdGroup, cmd.cmdID, out _))
                    {
                        if (IsBuilding)
                        {
                            string? str = this.buildManager?.CommandText;
                            cmd.cmdf = (uint)OLECMDF.OLECMDF_SUPPORTED;
                            if (str.IsPresent())
                            {
                                VsShellUtilities.SetOleCmdText(pCmdText, str);
                            }

                            if (cCmds == 1)
                            {
                                return VSConstants.S_OK;
                            }
                        }
                    }
                }
            }
            return (int)OLEConstants.OLECMDERR_E_NOTSUPPORTED;
        }

        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (this.IsEnabled && isDebugLaunchCommand(pguidCmdGroup, nCmdID, out var command))
            {
                log.LogVerbose($"Debugging command detected [CanDebug: {CanDebug}]");
                if (CanDebug)
                {
                    Debug.Assert(buildManager is null);
                    buildManager = new(this.Solution,
                                       (IVsSolutionBuildManager2)this.SolutionBuildManager,
                                       this.debugger.Value,
                                       this.log);
                    buildManager.DebuggingStop += (o, e) =>
                    {
                        log.LogTrace("Debugging session end");
                        var item = (ProjectBuildManager)o;
                        var bm = Interlocked.CompareExchange(ref this.buildManager, null, item);
                        item?.Dispose();
                    };
                    if(buildManager.Build(this.projects, command))
                    {
                        return VSConstants.S_OK;
                    }
                    else
                    {
                        log.LogTrace("Project build failed. Fallback to default");
                        buildManager.Dispose();
                        buildManager = null;
                    }
                }
            }
            return (int)OLEConstants.OLECMDERR_E_NOTSUPPORTED;
        }

        public void Disable()
        {
            if (this.cookie != 0)
            {
                ProjectEvents.OnStartupProjectChanged -= onStartupProjectChanged;
                SolutionEvents.OnBeforeCloseSolution -= onSolutionClose;
                VsUtils.RunOnUIThread(() =>
                {
                    this.RegisterPriorityCommandTarget
                        .UnregisterPriorityCommandTarget(this.cookie)
                        .RequireOk();
                    this.cookie = 0;
                });
            }
        }

        public void Enable()
        {
            if (this.cookie == 0)
            {
                this.initStartupProject();
                ProjectEvents.OnStartupProjectChanged += onStartupProjectChanged;
                SolutionEvents.OnAfterCloseSolution += onSolutionClose;
                VsUtils.RunOnUIThread(() =>
                {
                    this.RegisterPriorityCommandTarget
                    .RegisterPriorityCommandTarget(0, this, out this.cookie)
                    .RequireOk();
                });
            }
        }

        private void onSolutionClose(object sender, EventArgs e)
        {
            this.projects.Clear();
        }

        private void initStartupProject()
        {
            this.startupProject = VsUtils.RunOnUIThread(() =>
            {
                return this.Solution.GetStartupProjects()
                .Select(x => tryFindMsTestProject(x.Hierarchy)!)
                .Where(x => x is not null)
                .ToList();
            });
        }

        private IMsTestProject? tryFindMsTestProject(IVsHierarchy projectHier)
        {
            return this.projects.Find(x => x.Is(projectHier));
        }

        private void onStartupProjectChanged(object sender, StartupProjectEventArgs e)
        {
            var lst = e.Hierarchies
                .Select(x => tryFindMsTestProject(x.Hierarchy)!)
                .Where(x => x is not null)
                .ToList();
            this.startupProject = lst;
            if (lst.Count > 0)
            {
                this.Enable();
            }
        }

        public void Remove(IMsTestProject msTestProject)
        {
            bool disable;
            lock (this.projects)
            {
                disable = this.projects.Count == 0;
            }

            if (disable)
            {
                this.Disable();
            }
        }

        public void Add(IMsTestProject msTestProject)
        {
            bool enable;
            lock (this.projects)
            {
                this.projects.Add(msTestProject);
                enable = this.projects.Count == 1;
            }
            if (enable)
            {
                this.Enable();
            }
        }

        public void Dispose()
        {
            this.buildManager?.Dispose();
            GC.SuppressFinalize(this);
        }

        #region Helpers

        private bool isDebugLaunchCommand(Guid pguidCmdGroup, uint cmdID, out LaunchCommand command)
        {
            if (isStartDebugTarget(pguidCmdGroup, cmdID))
            {
                command = LaunchCommand.LaunchDebugTarget;
            }
            else if (isStartProject(pguidCmdGroup, cmdID))
            {
                command = LaunchCommand.LaunchProjectSelection;
            }
            else if (isStartWithoutDebugging(pguidCmdGroup, cmdID))
            {
                command = LaunchCommand.StartWithoutDebugging;
            }
            else if (isStepIntoNewInstance(pguidCmdGroup, cmdID))
            {
                command = LaunchCommand.StepIntoNewInstance;
            }
            else
            {
                command = LaunchCommand.None;
                return false;
            }

            return true;
        }

        private static bool isStepIntoNewInstance(Guid pguidCmdGroup, uint cmdID)
            => pguidCmdGroup == Constants.DebugCommands.ProjectDebugContextMenuCmdSet
            && cmdID == Constants.DebugCommands.StepIntoNewInstanceCommandId;

        private static bool isStartWithoutDebugging(Guid pguidCmdGroup, uint cmdID)
            => pguidCmdGroup == Constants.DebugCommands.ProjectDebugContextMenuCmdSet
            && cmdID == Constants.DebugCommands.StartWithoutDebuggingCommandId;

        private static bool isStartProject(Guid pguidCmdGroup, uint cmdID)
            => pguidCmdGroup == Constants.DebugCommands.ProjectDebugContextMenuCmdSet
            && cmdID == Constants.DebugCommands.StartProjectCommandId;

        private static bool isStartDebugTarget(Guid pguidCmdGroup, uint cmdID)
            => pguidCmdGroup == Constants.DebugCommands.DebugCommandSet
            && cmdID == Constants.DebugCommands.StartDebugTargetCommandId;

        #endregion Helpers
    }
}
