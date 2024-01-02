namespace Launcher.ViewModels
{
    internal sealed partial class LaunchProfileSaver
    {
        sealed class LaunchProfile : ILaunchProfile2, IWritableLaunchProfile
        {
            private readonly Dictionary<string, object> otherSettings = new();
            private readonly Dictionary<string, string> envVars = new();

            public ImmutableArray<(string Key, string Value)> EnvironmentVariables => envVars
                .Select(x => (x.Key, x.Value))
                .ToImmutableArray();
            public ImmutableArray<(string Key, object Value)> OtherSettings
                => otherSettings.Select(x => (x.Key, x.Value))
                .ToImmutableArray();
            public string? Name { get; set; }
            public string? CommandName { get; set; } = Constants.ProfileCommandName;
            public string? ExecutablePath { get; set; }
            public string? CommandLineArgs { get; set; }
            public string? WorkingDirectory { get; set; }
            public bool LaunchBrowser { get; set; }
            public string? LaunchUrl { get; set; }
            ImmutableDictionary<string, string>? ILaunchProfile.EnvironmentVariables
                => this.envVars.ToImmutableDictionary();
            Dictionary<string, string> IWritableLaunchProfile.EnvironmentVariables => this.envVars;
            ImmutableDictionary<string, object>? ILaunchProfile.OtherSettings
                => this.otherSettings.ToImmutableDictionary();
            Dictionary<string, object> IWritableLaunchProfile.OtherSettings
                => this.otherSettings;

            ILaunchProfile IWritableLaunchProfile.ToLaunchProfile() => this;
        }
    }
}
