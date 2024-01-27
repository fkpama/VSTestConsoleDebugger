using System.Windows;
using System.Windows.Interop;
using Launcher.Models;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.ProjectSystem.VS.PropertyPages;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Launcher.ViewModels
{
    internal sealed partial class LaunchProfileSaver : ILaunchProfileSaver
    {
        private readonly Window window;
        private readonly ProjectSelectorViewModel viewModel;
        private readonly ILaunchSettingsProvider settingsProvider;
        private readonly ITargetSerializer serializer;
        private readonly SVsServiceProvider services;
        private readonly IProjectThreadingService threadingService;

        internal LaunchProfileSaver(
            Window window,
            ProjectSelectorViewModel viewModel,
            ILaunchSettingsProvider settingsProvider,
            ITargetSerializer serializer,
            SVsServiceProvider service,
            IProjectThreadingService threadingService)
        {
            this.window = window;
            this.viewModel = viewModel;
            this.settingsProvider = settingsProvider;
            this.serializer = serializer;
            this.services = service;
            this.threadingService = threadingService;
        }
        public bool HasProfile(Target targetSelectionResult)
        {
            var profiles = this.settingsProvider.CurrentSnapshot?
                .Profiles
                .Where(x => x.IsVsTestConsole())
                .ToArray();
            if (profiles is null || profiles.Length == 0)
                return false;

            return profiles.Any(x => x.GetEntry(this.serializer) == targetSelectionResult);
        }

        public async Task SaveAsync(Target target, CancellationToken cancellationToken)
        {
            var serializedTarget = this.serializer.Serialize(target);
            var dispatcher = this.window.Dispatcher;
            var name = getNameSuggestion(target);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = new GetProfileNameDialog(this.services,
                                                  this.threadingService,
                                                  name,
                                                  x => true);

            var helper = new WindowInteropHelper(this.window);
            try
            {
                this.viewModel.Cancelled = true;
                this.window.Opacity = 0;
                WindowHelper.ShowModal(window, helper.Handle);
                var profileName = window.ProfileName;
                var profile = new LaunchProfile
                {
                    Name = profileName,
                    CommandName = Constants.ProfileCommandName,
                };
                profile.SetTarget(serializedTarget);
                await this.settingsProvider.AddOrUpdateProfileAsync(profile, false);
                this.window.Close();
            }
            catch (Exception ex)
            {
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                this.services.GetSolution().OpenLaunchProfileSettingsWindow();
            });
            await Task.Delay(500);

            var profileWIndow = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(x => x.GetType().Name.EqualsOrd("LaunchProfilesWindow"));
            if (profileWIndow is not null)
            {
                profileWIndow.Closed += onProfilesWindowClosed;
            }

        }

        private void onProfilesWindowClosed(object sender, EventArgs e)
        {
        }

        private string getNameSuggestion(Target target)
        {
            return "VSTest Console ()";
        }
    }
}
