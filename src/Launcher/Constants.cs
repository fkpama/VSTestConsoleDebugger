namespace Launcher
{
    public static class MsTestAdapterConstants
    {
        public const string PackageGuidString = "2b335ceb-bb9b-4eaa-bee1-9b178047909f";
    }
    internal static class Constants
    {
        internal const string DebuggerName = "VSTest.Console";
        internal const string GenericDebuggerName = "VSTest.Console.Generic";
        internal const string TestPlatformPackageName = "Microsoft.TestPlatform.ObjectModel";
        internal const string PackageReferenceItemGroup = "PackageReference";
        internal const string VsTestConsoleCapability = "VsTest_Console";
        internal const string GenericVsTestConsoleAsProfileCapability = "VsTest_Console_As_Profile";
        internal const string ProfileCommandName = "VSTestConsole";
        internal const string DefaultVsTestConsoleExeRelativeLocation = @"CommonExtensions\Microsoft\TestWindow\vstest.console.exe";
        internal static Task<ImmutableHashSet<string>> EmptyCapabilitiesResult = Task.FromResult(Empty.CapabilitiesSet);
        internal const string? TestAdapterFileExtension = ".TestAdapter";
        internal const string LoggerPaneIdString = "F276C136-E1D3-4F32-ADA4-8CF78AAB828C";

        public readonly static Guid LoggerPaneId = new(LoggerPaneIdString);

        internal static Guid PackageId => new(MsTestAdapterConstants.PackageGuidString);

        internal static class GenericDebuggerProperties
        {
            internal const string TargetPath = "targetPath";
            internal const string TargetType = "targetType";
        }

        internal static class VsTestEnv
        {
            public const string NoBreakPoint = "VSTEST_DEBUG_NOBP";
            public const string HostDebug = "VSTEST_HOST_DEBUG";
        }

        internal static class ProfileParams
        {
            internal const string
                ExePath                = "executablePath",
                WorkingDir             = "workingDirectory",
                CommandLineArguments   = "commandLineArgs",
                EnvironmentVariables   = "environmentVariables",
                SettingsFilePath       = "settingsFilePath",
                AutoDetectSettingsFile = "autoDetectSettingsFile",
                ExeTarget              = "exeTarget",
                ProjectTarget          = "projectTarget",
                Target                 = "target",
                TargetType             = "targetType"
                ;

        }

        internal static class DebugCommands
        {
            internal static Guid DebugCommandSet               = new("{6E87CFAD-6C05-4ADF-9CD7-3B7943875B7C}");
            internal static Guid ProjectDebugContextMenuCmdSet = new("{1496A755-94DE-11D0-8C3F-00C04FC2AAE2}");
            internal const int StartDebugTargetCommandId       = 0x101;
            internal const int StartProjectCommandId           = 0x164;
            internal const int StartWithoutDebuggingCommandId  = 0x167;
            internal const int StepIntoNewInstanceCommandId    = 0x165;

            //internal static Guid ClassCommandViewCmdSet = new("{6E87CFAD-6C05-4ADF-9CD7-3B79438}");
        }
    }
}
