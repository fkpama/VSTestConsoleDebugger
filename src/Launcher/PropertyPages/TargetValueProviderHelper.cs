using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Launcher.PropertyPages;
partial class TargetValueProvider
{
    internal interface ITargetValueProviderHelper
    {
        string ProjectDirectory { get; }
        IEnumerable<IVsProject> GetAllValidProjectTargets();
        TargetValueProviderHelper.VsProjectInfos? GetCachedProject(Guid projectId);
        //string MakeFullPath(string filePath);
        //string MakeRelative(string filePath);
        IVsHierarchy? FindProjectOfGuid(Guid projectId);
        Guid FindGuidOfProjectFile(string path);
    }

    internal sealed class TargetValueProviderHelper : ITargetValueProviderHelper
    {
        internal readonly struct VsProjectInfos
        {
            internal required string FilePath { get; init; }
            internal required string Name { get; init; }
            internal required Guid Id { get; init; }
            internal required readonly IVsProject Project { get; init; }
        }

        private readonly ConfiguredProject project;
        private readonly IServiceProvider services;
        private readonly IVsSolution solution;
        private readonly OrderPrecedenceImportCollection<IVsProject> projects;
        private readonly IProjectThreadingService threading;
        private readonly AsyncLazy<Dictionary<Guid, VsProjectInfos>> lazyProjects;
        private string? projectDir;

        public string ProjectDirectory
        {
            get
            {
                this.projectDir ??= Path.GetDirectoryName(this.project.UnconfiguredProject.FullPath);
                return this.projectDir;
            }
        }

        public TargetValueProviderHelper(ConfiguredProject project,
            IProjectThreadingService projectThreadingService,
            IServiceProvider services,
            [ImportMany(ExportContractNames.VsTypes.IVsProject)]
        IEnumerable<Lazy<IVsProject, IOrderPrecedenceMetadataView>> vsProjects)
        {
            this.project = project;
            this.services = services;
            this.solution = services.GetService<SVsSolution, IVsSolution>();
            this.projects = vsProjects.ToImportCollection(project);
            this.threading = projectThreadingService;
            this.lazyProjects = new(async () =>
            {
                await projectThreadingService.SwitchToUIThread();
                var cancellation = VsShellUtilities.ShutdownToken;
                var projects = this.solution.GetAllValidProjectTargets(this.projects.First().Value);
                var dict = new Dictionary<Guid, VsProjectInfos>();
                foreach (var project in projects)
                {
                    var name = project.GetName();
                    var id = project.GetProjectGuid();
                    var doc = project.GetMkDocument();
                    dict.Add(id, new()
                    {
                        Id = id,
                        Name = name,
                        Project = project,
                        FilePath = doc
                    });
                }
                return dict;
            }, projectThreadingService.JoinableTaskFactory);
        }

        public IEnumerable<IVsProject> GetAllValidProjectTargets()
        {
            var projects = this.solution.GetAllValidProjectTargets(this.projects.First().Value);
            return projects;
        }
        public VsProjectInfos? GetCachedProject(Guid projectId)
        {
            this.lazyProjects.GetValue()
                .TryGetValue(projectId, out var infos);
            return infos;
        }

        public IVsHierarchy? FindProjectOfGuid(Guid projectId)
            => this.threading.JoinableTaskFactory.Run(async () =>
            {
                var sp = ServiceProvider.GlobalProvider;
                await this.threading.SwitchToUIThread();
                var sln = sp.GetSolution();
                try
                {
                    return sln.GetProjectOfGuid(projectId);
                }
                catch
                {
                    // TODO: Log
                    return null;
                }
            });

        public Guid FindGuidOfProjectFile(string path)
            => this.threading.JoinableTaskFactory.Run(async () =>
            {
                var sp = ServiceProvider.GlobalProvider;
                await this.threading.SwitchToUIThread();
                var sln = (IVsSolution5)sp.GetSolution();
                return sln.GetGuidOfProjectFile(path);
            });
    }
}
