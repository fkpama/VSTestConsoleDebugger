using System.ComponentModel;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;

namespace Launcher.Settings
{
    [TypeConverter(typeof(EnumNameTypeConverter))]
    public enum LogLevel
    {
        [LocalizedName(typeof(Resources), nameof(Resources.LogLevel_Info))]
        Info    = 1,
        [LocalizedName(typeof(Resources), nameof(Resources.LogLevel_Warning))]
        Warning = 2,
        [LocalizedName(typeof(Resources), nameof(Resources.LogLevel_Trace))]
        Trace   = 3,
        [LocalizedName(typeof(Resources), nameof(Resources.LogLevel_Error))]
        Error   = 5
    }
    public sealed class MsTestAdapterDebuggerOptionPage : DialogPage
    {
        [LocalizedCategory(typeof(Resources), nameof(Resources.OptionCategory_General))]
        [LocalizedDisplayName(typeof(Resources), nameof(Resources.Option_VSTestConsolePath_DisplayName))]
        [LocalizedDescription(typeof(Resources), nameof(Resources.Option_VSTestConsolePath_Description))]
        public string? VsTestConsolePath { get; set; }

        [LocalizedCategory(typeof(Resources), nameof(Resources.OptionCategory_General))]
        [LocalizedDisplayName(typeof(Resources), nameof(Resources.Option_LogLevel_DisplayName))]
        [LocalizedDescription(typeof(Resources), nameof(Resources.Option_LogLevel_Description))]
        [DefaultValue(LogLevel.Warning)]
        public LogLevel LogLevel { get; set; }
    }
}