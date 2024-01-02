using System.ComponentModel;

namespace Launcher
{
    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum ProjectSelectorAction
    {
        [Description("Executable")]
        Executable,

        [Description("Project")]
        Project,
    }
}
