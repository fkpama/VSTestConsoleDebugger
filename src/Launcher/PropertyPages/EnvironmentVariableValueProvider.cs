using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.PropertyPages.Designer;
using Sodiware.VisualStudio.ProjectSystem.PropertyPages;

namespace Launcher.PropertyPages
{
    [Export(typeof(INameValuePairListEncoding))]
    [ExportMetadata("Encoding", EncodingName)]
    [AppliesTo(Constants.VsTestConsoleCapability)]
    internal class TestAdapterKeyValueEncoding : NameValuePairEncodingBase
    {
        internal const string EncodingName = "TestAdapterKeyValueEncoding";
    }

    [ExportLaunchProfileExtensionValueProvider(Constants.ProfileParams.EnvironmentVariables, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
    [AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
    internal sealed class EnvironmentVariableValueProvider : EnvironmentVariableValueProviderBase
    {
        [ImportingConstructor]
        public EnvironmentVariableValueProvider()
            : base(TestAdapterKeyValueEncoding.EncodingName)
        {
        }
    }
}
