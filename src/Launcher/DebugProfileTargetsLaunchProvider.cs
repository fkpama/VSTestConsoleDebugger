using Launcher.Debugger;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System.Threading.Tasks.Dataflow;
using Launcher.Settings;
using Launcher.Models;

namespace Launcher
{
    [Export(typeof(IDebugProfileLaunchTargetsProvider))]
    [AppliesTo(Constants.VsTestConsoleCapability)]
    internal sealed class DebugProfileTargetsLaunchProvider
        : DebugLaunchProviderBase,
        IDebugProfileLaunchTargetsProvider,
        IDebugProfileLaunchTargetsProvider2,
        IDebugProfileLaunchTargetsProvider3,
        IDebugProfileLaunchTargetsProvider4,
        IDebugLaunchHost
    {
        private readonly ConfiguredProject project;
        private readonly ITestAdapterSettings adapterSettings;
        private readonly MruFileSerializer serializer;
        private readonly IServiceProvider services;
        private readonly Lazy<IDebuggerImageTypeService> imageTypeService;
        private readonly OrderPrecedenceImportCollection<IVsHierarchy> hierCollection;
        private DebugLauncherHelper? launcher;
        private readonly ActionBlock<ILaunchSettings> checkDependencyFirstLink;

        internal DebugLauncherHelper Launcher
        {
            get
            {
                if (launcher is null)
                {
                    var outputGroups = this.project.Services.OutputGroups;
                    Guard.Debug.NotNull(outputGroups);
                    this.launcher = new(this.services,
                                        this.adapterSettings,
                                        this,
                                        outputGroups,
                                        this.ProjectHier,
                                        this.project.Services.ThreadingPolicy,
                                        imageTypeService);
                }
                return this.launcher;
            }
        }

        public IVsHierarchy ProjectHier => this.hierCollection.First().Value;

        [ImportingConstructor]
        public DebugProfileTargetsLaunchProvider(
            ConfiguredProject project,
            ITestAdapterSettings adapterSettings,
            ILaunchSettingsProvider settingsProvider,
            MruFileSerializer serializer,
            IOutputGroupsService outputGroups,
            [ImportMany(ExportContractNames.VsTypes.IVsHierarchy)]
            IEnumerable<Lazy<IVsHierarchy, IOrderPrecedenceMetadataView>> vsProjects,
            [Import(typeof(SVsServiceProvider))]IServiceProvider services,
            Lazy<IDebuggerImageTypeService> imageTypeService)
            : base(project)
        {
            this.project = project;
            this.adapterSettings = adapterSettings;
            this.serializer = serializer;
            this.services = services;
            this.imageTypeService = imageTypeService;
            this.hierCollection = vsProjects.ToImportCollection(project);
            settingsProvider.SourceBlock
                .LinkTo(checkDependencyFirstLink = new(onLaunchSettingsChanged));
        }

        private async Task onLaunchSettingsChanged(ILaunchSettings settings)
        {
            if(settings.ActiveProfile?.IsVsTestConsole() != true)
            {
                return;
            }

            var target = settings.ActiveProfile.GetTarget(this.serializer);
            if (target.IsMissing())
            {
                return;
            }

            var entry = this.serializer.TryDeserializeEntry(target!);
            if (entry.Mode != ProjectSelectorAction.Project || entry.Id is null)
            {
                return;
            }

            var solution = this.services.GetSolution();

            await project.Services.ThreadingPolicy.SwitchToUIThread();
            var otherProject = solution.GetProjectOfGuid(entry.Id.Value);
            if (otherProject is null)
            {
                return;
            }
            try
            {
                otherProject.GetVsProject()
                    .AddBuildDependency(this.ProjectHier.GetVsProject());
            }
            catch (Exception)
            {

            }
        }

        public Task OnAfterLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            return Task.CompletedTask;
        }

        public Task OnBeforeLaunchAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            var target = profile.GetTarget(this.serializer);
            if (target.IsMissing())
            {
                throw new NotImplementedException();
            }
            var cancellationToken = CancellationToken.None;
            var entry = this.serializer.TryDeserializeEntry(target!);
            var settings = await this.Launcher.LaunchAsync(new()
            {
                Operation = launchOptions,
                Target = entry,
                WorkingDir = profile.WorkingDirectory,
                Environment = profile.EnvironmentVariables
            }, cancellationToken).NoAwait();
            //return new[] { settings };
            return settings is not null ? new[] { settings } : Array.Empty<IDebugLaunchSettings>();
        }

        public bool SupportsProfile(ILaunchProfile profile)
        {
            if (!profile.IsVsTestConsole()) return false;

            return profile.GetTarget(this.serializer).IsPresent();
        }

        Task<bool> IDebugProfileLaunchTargetsProvider3.CanBeStartupProjectAsync(DebugLaunchOptions launchOptions, ILaunchProfile profile)
        {
            return TaskResults.True;
        }

        Task IDebugProfileLaunchTargetsProvider4
            .OnAfterLaunchAsync(DebugLaunchOptions launchOptions,
                                ILaunchProfile profile,
                                IReadOnlyList<VsDebugTargetProcessInfo> processInfos)
        {
            if (processInfos?.Count > 0)
            {
                var pids = processInfos.Select(x => x.dwProcessId).ToArray();
                this.Launcher.RegisterPids(pids);
            }
            return Task.CompletedTask;
        }

        Task<IReadOnlyList<IDebugLaunchSettings>> IDebugProfileLaunchTargetsProvider2
            .QueryDebugTargetsForDebugLaunchAsync(DebugLaunchOptions launchOptions,
                                                  ILaunchProfile profile)
            => QueryDebugTargetsAsync(launchOptions, profile);

        Task IDebugProfileLaunchTargetsProvider4
            .OnBeforeLaunchAsync(
            DebugLaunchOptions launchOptions,
            ILaunchProfile profile,
            IReadOnlyList<IDebugLaunchSettings> debugLaunchSettings)
            => Task.CompletedTask;

        Task IDebugLaunchHost.LaunchAsync(DebugLaunchSettings settings, CancellationToken cancellationToken)
        {
            return base.LaunchAsync(settings);
        }

        public override Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            throw new NotImplementedException();
        }

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        {
            throw new NotImplementedException();
        }
    }
}
