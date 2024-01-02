using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Launcher.PropertyPages
{
    [PartCreationPolicy(CreationPolicy.Shared)]
    [ExportDynamicEnumValuesProvider(nameof(ProjectTargetListProvider))]
    [AppliesTo(Constants.VsTestConsoleCapability)]
    internal sealed class ProjectTargetListProvider : IDynamicEnumValuesProvider,
        IDynamicEnumValuesGenerator
    {
        private readonly IProjectThreadingService threadingService;
        private readonly IServiceProvider serviceProvider;
        private readonly OrderPrecedenceImportCollection<IVsProject> projectCollection;
        private readonly List<IEnumValue> enumValues = new();

        internal IVsProject IVsProject
        {
            get => this.projectCollection.First().Value;
        }

        internal IVsSolution IVsSolution { get; }

        public bool AllowCustomValues { get; }

        [ImportingConstructor]
        public ProjectTargetListProvider(
            ConfiguredProject project,
            IProjectThreadingService threadingService,
            [ImportMany(ExportContractNames.VsTypes.IVsProject)]
            IEnumerable<Lazy<IVsProject, IOrderPrecedenceMetadataView>> vsProjects,
            [Import(typeof(SVsServiceProvider))]
            IServiceProvider serviceProvider)
        {
            this.threadingService = threadingService;
            this.serviceProvider = serviceProvider;
            this.projectCollection = vsProjects.ToImportCollection(project);
            this.IVsSolution = serviceProvider.GetSolution();
        }

        public async Task<ICollection<IEnumValue>> GetListedValuesAsync()
        {
            if (!this.threadingService.IsOnMainThread)
                await this.threadingService.SwitchToUIThread();

            var cancellationToken = VsShellUtilities.ShutdownToken;

            var lst = new List<IEnumValue>();

            foreach (var project in this.IVsSolution
                .GetAllValidProjectTargets(this.IVsProject))
            {
                var name = project.GetName();
                var id = project.GetProjectGuid();
                var value = new PageEnumValue(new()
                {
                    DisplayName = name,
                    Name = id.ToString()
                });
                lst.Add(value);
            }

            return lst;
        }

        public Task<IDynamicEnumValuesGenerator> GetProviderAsync(IList<NameValuePair>? options)
        {
            return Task.FromResult<IDynamicEnumValuesGenerator>(this);
        }

        public Task<IEnumValue?> TryCreateEnumValueAsync(string userSuppliedValue)
        {
            return Task.FromResult<IEnumValue?>(null);
        }
    }
}
