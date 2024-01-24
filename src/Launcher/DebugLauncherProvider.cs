using Launcher.Controls;
using Launcher.Debugger;
using Launcher.Models;
using Launcher.Settings;
using Launcher.ViewModels;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using DialogWindow = Launcher.Controls.DialogWindow;

namespace Launcher
{
    [ExportDebugger(Constants.GenericDebuggerName)]
    [AppliesTo($"{Constants.VsTestConsoleCapability} & !{Constants.GenericVsTestConsoleAsProfileCapability}")]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal sealed class DebugLauncherProvider : DebugLaunchProviderBase, IDebugLaunchHost
    {
        private readonly ILaunchSettingsProvider settingsProvider;
        private readonly ITestAdapterSettings adapterSettings;
        private readonly Lazy<IDebuggerImageTypeService> debuggerImageTypeService;
        //private readonly IServiceProvider serviceProvider;
        private readonly IOutputGroupsService outputGroupsService;
        private DebugLauncherHelper? launcher;
        private readonly OrderPrecedenceImportCollection<IVsProject> projectCollection;
        private string? vsTestConsolePath;
        private readonly TestAdapterMruService mruService;
        internal IVsProject IVsProject
        {
            get => this.projectCollection.First().Value;
        }
        public string VsTestConsolePath
        {
            get
            {
                vsTestConsolePath ??= Utils.FindVsTestConsole();
                return vsTestConsolePath;
            }
        }

        [ImportingConstructor]
        internal DebugLauncherProvider(ConfiguredProject project,
                                       ILaunchSettingsProvider settings,
                                       ITestAdapterSettings adapterSettings,
                                       Lazy<IDebuggerImageTypeService> debuggerImageTypeService,
                                       [Import(typeof(SVsServiceProvider))]
                                       IServiceProvider serviceProvider,
                                       [ImportMany(ExportContractNames.VsTypes.IVsProject)]
                                       IEnumerable<Lazy<IVsProject, IOrderPrecedenceMetadataView>> vsProjects,
                                       MruFileSerializer mruFileSerializer,
                                       IOutputGroupsService outputGroupsService)
            : base(project)
        {
            this.settingsProvider = settings;
            this.adapterSettings = adapterSettings;
            this.debuggerImageTypeService = debuggerImageTypeService;
            //this.serviceProvider = serviceProvider;
            this.outputGroupsService = outputGroupsService;
            this.projectCollection = vsProjects.ToImportCollection(project);
            this.mruService = new(project, mruFileSerializer, serviceProvider);
        }

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
        {
            return TaskResults.True;
        }

        public override async Task LaunchAsync(DebugLaunchOptions launchOptions)
        {
            var cancellationToken = VsShellUtilities.ShutdownToken;
            Target entry;
            try
            {
                entry = await selectProjectAsync(cancellationToken).NoAwait();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (entry.IsEmpty) return;

            Task.Run(async () =>
            {
                if (entry.Mode == ProjectSelectorAction.Project)
                {
                    // TODO: Build the project
                    await this.buildProjectAsync(entry).NoAwait();
                }

                if (!this.ThreadingService.IsOnMainThread)
                    await this.ThreadingService.SwitchToUIThread(cancellationToken);

                this.launcher ??= new(this.ServiceProvider,
                                      this.adapterSettings,
                                      this,
                                      outputGroupsService,
                                      (IVsHierarchy)this.IVsProject,
                                      this.ThreadingService,
                                      debuggerImageTypeService);

                var settings = await this.launcher.LaunchAsync(new()
                {
                    Target = entry,
                    Operation = launchOptions
                }, cancellationToken).NoAwait();

                if (!this.ThreadingService.IsOnMainThread)
                    await this.ThreadingService.SwitchToUIThread(cancellationToken);
                await base.LaunchAsync(settings)
                    .WithCancellationToken(cancellationToken)
                    .NoAwait();
            }).Forget();
        }

        private async Task buildProjectAsync(Target entry)
        {
            if (entry.Id is null)
            {
                return;
            }
            if (!this.ThreadingService.IsOnMainThread)
                await this.ThreadingService.SwitchToUIThread();

            var project = this.ServiceProvider
                .GetSolution()
                .GetProjectOfGuid(entry.Id.Value)?
                .GetVsProject();

            if (project is not null)
                await project.BuildAsync().NoAwait();
        }

        public override Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            throw new NotImplementedException();
        }

        private async Task<Target> selectProjectAsync(CancellationToken cancellationToken = default)
        {

            var lstTask = this.mruService.GetEntriesAsync(cancellationToken);
            if (!ThreadingService.IsOnMainThread)
                await this.ThreadingService.SwitchToUIThread(cancellationToken);

            var entries = new List<EntryViewModel>();
            var sln = this.ServiceProvider.GetService<SVsSolution, IVsSolution>();
            //var myId = await this.ProjectId.GetValueAsync(cancellationToken);
            var uishell = this.ServiceProvider.GetService<SVsUIShell, IVsUIShell>();
            foreach (var project in sln.GetAllValidProjectTargets(this.IVsProject))
            {
                var id = project.GetProjectGuid();
                var name = project.GetName();
                var icon = project.GetIcon();
                entries.Add(new(name, id) { Bitmap = icon, });
            }

            var lst = lstTask.Result
                .Where(x => x.Mode == ProjectSelectorAction.Executable
                && x.TargetPath.IsPresent())
                .Select(x => x.TargetPath);
            var vms = lstTask.Result.Select(x => new EntryViewModel(x)).ToList();
            var viewModel = new ProjectSelectorViewModel(this.ThreadingService.JoinableTaskFactory,
                                                         vms,
                                                         entries,
                                                         mruList: lst);
            var window = new DialogWindow
            {
                Content = new ProjectSelectorControl{ DataContext = viewModel }
            };
            var profileSaver = new LaunchProfileSaver(window,
                                                      viewModel,
                                                      this.settingsProvider,
                                                      this.mruService.Serializer,
                                                      this.ServiceProvider,
                                                      this.ThreadingService);
            viewModel.LaunchProfileSaver = profileSaver;
            uishell.GetDialogOwnerHwnd(out var phwnd).RequireOk();
            uishell.EnableModeless(0);

            try
            {
                WindowHelper.ShowModal(window, phwnd);
            }
            finally
            {
                uishell.EnableModeless(1);
            }

            if (viewModel.Cancelled)
            {
                return Target.Empy;
            }

            var path = viewModel.GetPath();
            var entry =new Target(viewModel.Mode, path, viewModel.SelectedEntry?.ProjectId);
            this.mruService.PushEntryAsync(entry, cancellationToken).Forget();
            return entry;
        }

        Task IDebugLaunchHost.LaunchAsync(DebugLaunchSettings settings, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
