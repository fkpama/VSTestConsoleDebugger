using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem.Properties;

namespace Launcher.PropertyPages
{
    [ExportLaunchProfileExtensionValueProvider(Constants.ProfileParams.ExePath, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
    [AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
    internal sealed class ExecutablePathValueProvider : ILaunchProfileExtensionValueProvider
    {
        public string OnGetPropertyValue(string propertyName, ILaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
            => launchProfile.ExecutablePath!;

        public void OnSetPropertyValue(string propertyName, string propertyValue, IWritableLaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
            => launchProfile.ExecutablePath = propertyValue;
    }

    [AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
    [ExportLaunchProfileExtensionValueProvider(Constants.ProfileParams.WorkingDir, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
    internal sealed class WorkingDirValueProvider : ILaunchProfileExtensionValueProvider
    {
        public string OnGetPropertyValue(string propertyName, ILaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
            => launchProfile.WorkingDirectory!;

        public void OnSetPropertyValue(string propertyName, string propertyValue, IWritableLaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
            => launchProfile.WorkingDirectory = propertyValue;
    }

    [ExportLaunchProfileExtensionValueProvider(Constants.ProfileParams.CommandLineArguments, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
    [AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
    internal class CommandLineArgsValueProvider : ILaunchProfileExtensionValueProvider
    {
        public string OnGetPropertyValue(string propertyName, ILaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
            => launchProfile.CommandLineArgs!;

        public void OnSetPropertyValue(string propertyName, string propertyValue, IWritableLaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
            => launchProfile.CommandLineArgs = propertyValue;
    }

    [ExportLaunchProfileExtensionValueProvider(Constants.ProfileParams.SettingsFilePath, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
    [AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
    internal class SettingsFilePathValueProvider : LaunchProfileSettingsValueProvider<string>
    { }

    [ExportLaunchProfileExtensionValueProvider(Constants.ProfileParams.AutoDetectSettingsFile, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
    [AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
    internal class AutoDetectSettingsFilePathValueProvider : LaunchProfileSettingsValueProvider<bool>
    { }

    public abstract class LaunchProfileSettingsValueProvider<T> : ILaunchProfileExtensionValueProvider
    {
        protected virtual bool AllowWhitespace { get; }
        protected virtual bool RemoveFalseValueFields { get; } = true;
        protected LaunchProfileSettingsValueProvider() { }
        public string OnGetPropertyValue(string propertyName, ILaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
        {
            return (launchProfile.OtherSettings.GetVal<T>(propertyName)?.ToString()).IfMissing(string.Empty);
        }

        public void OnSetPropertyValue(string propertyName, string propertyValue, IWritableLaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
        {
            bool remove = AllowWhitespace
                ? propertyValue.IsNullOrEmpty()
                : propertyValue.IsEmptyOrWhitespace();
            if (!remove && typeof(T) == typeof(bool) && RemoveFalseValueFields)
            {
                try
                {
                    if (!XmlUtil.ToBoolean(propertyValue))
                    {
                        remove = true;
                    }
                }
                catch (FormatException) { }
            }
            if (remove)
            {
                launchProfile.OtherSettings.Remove(propertyName);
            }
            else
            {
                launchProfile.OtherSettings[propertyName] = propertyValue;
            }
        }
    }
}
