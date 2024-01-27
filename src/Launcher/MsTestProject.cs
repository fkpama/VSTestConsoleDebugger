using System.Threading.Tasks.Dataflow;
using Launcher;
using Launcher.Models;
using Microsoft.VisualStudio.Shell.Interop;

namespace Launcher
{
    internal interface IMsTestProject
    {
        IVsHierarchy? TargetProject { get; }
        bool Is(IVsHierarchy project);
    }
    internal sealed class MsTestProject : IMsTestProject
    {
        private readonly IVsProject project;
        private readonly ITargetSerializer serializer;
        private readonly Lazy<IVsSolution> solution;
        private ILaunchSettings? currentProfile;

        public ProjectSelectorAction Mode { get; private set; }

        public IVsHierarchy? TargetProject { get; private set; }

        internal MsTestProject(IVsProject project,
            ITargetSerializer serializer,
            Lazy<IVsSolution> solution)
        {
            this.project = project;
            this.serializer = serializer;
            this.solution = solution;
        }

        internal void OnProfileUpdated(ILaunchSettings? value)
        {
            this.currentProfile = value;
            IVsHierarchy? targetHierarchy = null;
            ProjectSelectorAction mode = 0;
            if (value?.ActiveProfile?.IsVsTestConsole() == true)
            {
                var target = value.ActiveProfile.GetEntry(this.serializer);
                mode = target.Mode;
                if (mode == ProjectSelectorAction.Project)
                {
                    targetHierarchy = Utils.GetTargetProject(this.solution.Value,
                                                             this.project.AsVsHierarchy(),
                                                             target);
                }
            }
            this.TargetProject = targetHierarchy;
            this.Mode = mode;
        }

        public bool Is(IVsHierarchy project)
            => VsUtils.IsSameProject(project, this.project);
    }
}
