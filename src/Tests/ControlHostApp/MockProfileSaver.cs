using Launcher;
using Launcher.ViewModels;

namespace ControlHostApp
{
    internal class MockProfileSaver : ILaunchProfileSaver
    {
        public bool HasProfile(Target target)
        {
            if (target.Mode == ProjectSelectorAction.Project)
            {
                return true;
            }
            return false;
        }

        public Task SaveAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}