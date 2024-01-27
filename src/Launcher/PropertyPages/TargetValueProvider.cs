using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Launcher.Models;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sodiware;
using Sodiware.IO;

namespace Launcher.PropertyPages;
[ExportLaunchProfileExtensionValueProvider(new[]
{
    Constants.ProfileParams.ProjectTarget,
    Constants.ProfileParams.ExeTarget,
    Constants.ProfileParams.TargetType,
}, ExportLaunchProfileExtensionValueProviderScope.LaunchProfile)]
[AppliesTo(Constants.GenericVsTestConsoleAsProfileCapability)]
internal sealed partial class TargetValueProvider : ILaunchProfileExtensionValueProvider
{
    sealed class TypeContext
    {
        internal ProjectSelectorAction Type;
        internal Guid ProjectId;
        internal string? Executable;
    }

    private readonly ConditionalWeakTable<string, TypeContext> targetTypes = new();
    //private readonly ConfiguredProject project;
    private readonly ITargetSerializer serializer;
    //private readonly Dictionary<ProjectSelectorAction, TypeContext> context = new();
    private readonly ITargetValueProviderHelper helper;

    [ImportingConstructor]
    public TargetValueProvider(
        ConfiguredProject project,
        ITargetSerializer serializer,
        IProjectThreadingService projectThreadingService,
        [Import(typeof(SVsServiceProvider))] IServiceProvider services,
        [ImportMany(ExportContractNames.VsTypes.IVsProject)]
        IEnumerable<Lazy<IVsProject, IOrderPrecedenceMetadataView>> vsProjects)
        : this(new TargetValueProviderHelper(project,
                                        projectThreadingService,
                                        services,
                                        vsProjects),
              serializer)
    { }
    public TargetValueProvider(ITargetValueProviderHelper helper, ITargetSerializer serializer)
    {
        this.helper = helper;
        this.serializer = serializer;
    }

    public string OnGetPropertyValue(string propertyName,
                                     ILaunchProfile launchProfile,
                                     ImmutableDictionary<string, object> globalSettings,
                                     Rule? rule)
    {
        var valueAsObject = launchProfile.GetTargetValue();
        Target entry = Target.Empy;
        if (valueAsObject is not null)
            deserializeValue(valueAsObject, out entry);

        if (!this.targetTypes.TryGetValue(launchProfile.Name.IfMissing(), out var box))
        {
            box = new()
            {
                Type = entry.Mode
            };

        }
        if (propertyName.EqualsOrd(Constants.ProfileParams.TargetType))
        {
            return box.Type.ToString();
        }

        if (valueAsObject is null) return string.Empty;

        var targetType = box.Type;
        if (propertyName.EqualsOrd(Constants.ProfileParams.ExeTarget))
        {
            if (targetType == ProjectSelectorAction.Project)
            {
                return string.Empty;
            }
            return entry.TargetPath;
        }

        if (targetType == ProjectSelectorAction.Executable)
        {
            return string.Empty;
        }

        string value = string.Empty;
        if (entry.Id.HasValue && entry.Id != Guid.Empty)
        {
            var project = this.helper.GetCachedProject(entry.Id.Value);
            if (project.HasValue)
                value = project.Value.Id.ToString();
        }
        return value;
    }

    private bool deserializeValue(object valueAsObject, out Target entry)
    {
        entry = Target.Empy;
        TargetModel? model = null;
        if (valueAsObject is IReadOnlyDictionary<string, object> values)
        {
            model = TargetModel.Deserialize(values);
        }
        else if (valueAsObject is string value)
        {
            entry = this.serializer.TryDeserializeEntry(value, false);
        }
        else if (valueAsObject is JObject jo)
        {
            var str = jo.ToString();
            if (str.IsMissing())
            {
                return false;
            }
            try
            {
                model = TargetModel.Deserialize(str);
                if (model is null)
                {
                    // TODO: Log
                    return false;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }
        else if (valueAsObject is JsonNode node)
        {
            model = node.Deserialize<TargetModel>();
            if (model is null)
            {
                // TODO: Log
                return false;
            }
            entry = model.GetEntry();
        }

        if (model is not null)
        {
            if (model.Type.IsMissing())
            {
                // Try to infer
                if (model.ProjectId.IsPresent()
                    && Guid.TryParse(model.ProjectId, out var projectId))
                {
                    var project = this.helper.FindProjectOfGuid(projectId);
                    if (project is not null)
                    {
                        entry = new(ProjectSelectorAction.Project,
                                    model.Path.IfMissing(),
                                    projectId);
                        return true;
                    }
                }

                if (model.Path.IsPresent())
                {
                    Assumes.NotNull(model.Path);
                    if (!Path.GetExtension(model.Path).EqualsOrdI(".dll"))
                    {
                        var path = this.makeFullPath(model.Path);
                        projectId = this.helper.FindGuidOfProjectFile(path);
                        entry = new(ProjectSelectorAction.Project,
                                    model.Path,
                                    projectId);
                        return true;
                    }
                }
            }
            if (model.Path.IsMissing())
                return false;
            entry = model.GetEntry();
        }

        return entry.IsEmpty;
    }

    public void OnSetPropertyValue(string propertyName, string propertyValue, IWritableLaunchProfile launchProfile, ImmutableDictionary<string, object> globalSettings, Rule? rule)
    {
        if (propertyName.EqualsOrd(Constants.ProfileParams.TargetType))
        {
            var value = (ProjectSelectorAction)Enum
                .Parse(typeof(ProjectSelectorAction), propertyValue);
            if (!this.targetTypes.TryGetValue(launchProfile.Name.IfMissing(), out var box))
            {
                box = new()
                {
                    Type = value
                };
                this.targetTypes.Add(launchProfile.Name.IfMissing(), box);
            }
            else if (box.Type != value)
            {
                this.removeTarget(launchProfile);
            }
            box.Type = value;
            return;
        }

        if (propertyValue.IsMissing())
        {
            this.removeTarget(launchProfile);
            return;
        }


        if (propertyName.EqualsOrd(Constants.ProfileParams.ProjectTarget))
        {
            Guid projectId = Guid.Parse(propertyValue);
            var proj = this.helper.GetCachedProject(projectId);
            Assumes.NotNull(proj);
            var filePath = this.makeRelative(proj.Value.FilePath);
            var model = new TargetModel
            {
                Path = filePath,
                ProjectId = projectId.ToString("D"),
                Type = ProjectSelectorAction.Project.ToString()
            };
            var obj = model.ToJObject();
            this.setPropertyValue(launchProfile, obj);
            this.cacheTargetType(launchProfile, ProjectSelectorAction.Project);
        }
        else
        {
            this.setPropertyValue(launchProfile, this.makeRelative(propertyValue));
            this.cacheTargetType(launchProfile, ProjectSelectorAction.Executable);
        }
    }

    private void cacheTargetType(IWritableLaunchProfile profile, ProjectSelectorAction project)
    {
        var name = profile.Name.IfMissing();
        if (!this.targetTypes.TryGetValue(name, out var box))
        {
            box = new();
            this.targetTypes.Add(name, box);
        }
        box.Type = project;
    }

    public void removeTarget(IWritableLaunchProfile launchProfile)
    {
        launchProfile.OtherSettings?.Remove(Constants.ProfileParams.Target);
    }

    void setPropertyValue(IWritableLaunchProfile launchProfile, object propertyValue)
    {
        launchProfile.OtherSettings[Constants.ProfileParams.Target] = propertyValue;
    }
    void setPropertyValue(IWritableLaunchProfile launchProfile, string propertyValue)
    {
        launchProfile.OtherSettings[Constants.ProfileParams.Target] = propertyValue;
    }
    private string makeFullPath(string filePath)
    {
        if (!Path.IsPathRooted(filePath))
            filePath = Path.Combine(this.helper.ProjectDirectory, filePath);
        return filePath;
    }
    private string makeRelative(string filePath)
    {
        if (Path.IsPathRooted(filePath))
        {
            filePath = PathUtils.MakeRelative(this.helper.ProjectDirectory, filePath);
        }
        return filePath;
    }

}
