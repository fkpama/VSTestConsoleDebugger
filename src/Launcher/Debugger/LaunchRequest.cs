namespace Launcher.Debugger;

internal readonly struct LaunchRequest
{
    public required Target Target { get; init; }
    public required DebugLaunchOptions Operation { get; init; }
    public string? WorkingDir { get; init; }
    public ImmutableDictionary<string, string>? Environment { get; init; }
    public string? AdditionalCommandLine { get; init; }
    public string? VsTestConsoleExePath { get; init; }
}
