using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Sodiware.IO;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Launcher.Models;

internal sealed class TargetModel
{
    static JsonSerializerSettings jsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };
    static JsonSerializerOptions textJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    private static WeakReference<JsonSerializer>? s_Instance;
    internal static JsonSerializer Serializer
    {
        [DebuggerStepThrough]
        get => SW.GetTarget(ref s_Instance, () => JsonSerializer.Create(jsonSettings));
    }

    public string? Type { get; set; }
    public string? ProjectId { get; set; }
    public string? Path { get; set; }
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid
    {
        get
        {
            if (this.Type.IsPresent()
                && Enum.TryParse(this.Type, out ProjectSelectorAction action))
            {
                if (action == ProjectSelectorAction.Executable)
                {
                    return Path.IsPresent()
                        && PathUtils.IsValidPath(Path);
                }
                else if (action == ProjectSelectorAction.Project
                    && ProjectId.IsPresent())
                {
                    return Guid.TryParse(this.ProjectId, out _);
                }
                else
                {
                    return false;
                }
            }
            else if (this.ProjectId.IsPresent()
                && Guid.TryParse(this.ProjectId, out _))
            { return true; }

            return Path.IsPresent() && PathUtils.IsValidPath(Path);
        }
    }

    [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ProjectSelectorAction EffectiveType
    {
        get
        {
            if (this.Type.IsPresent()
                && Enum.TryParse<ProjectSelectorAction>(this.Type, out var type))
            {
                return type;
            }
            else if (this.ProjectId.IsPresent()
                && Guid.TryParse(this.ProjectId, out _))
                return ProjectSelectorAction.Project;

            return ProjectSelectorAction.Executable;
        }
    }

    internal static TargetModel? Deserialize(string str)
        => System.Text.Json.JsonSerializer.Deserialize<TargetModel>(str, textJsonOptions);
    internal static TargetModel? Deserialize(IReadOnlyDictionary<string, object> values)
    {
        var str = JsonConvert.SerializeObject(values);
        return System.Text.Json.JsonSerializer.Deserialize<TargetModel>(str, textJsonOptions);
    }
    internal static TargetModel? Deserialize(JObject jobject)
    {
        return jobject.ToObject<TargetModel>(Serializer);
    }

    internal Target GetEntry()
    {
        if (this.Path.IsMissing())
        {
            throw new FormatException();
        }
        Assumes.NotNull(this.Path);
        Guid? id = null;
        var type = ProjectSelectorAction.Executable;
        if (this.Type.IsPresent()
            && Enum.TryParse(this.Type, out type))
        {
        }
        if (ProjectId.IsPresent()
            && Guid.TryParse(ProjectId, out var nid))
        {
            type = ProjectSelectorAction.Project;
            id = nid;
            this.Type = ProjectSelectorAction.Project.ToString();
        }
        return new(type, this.Path, id);
    }

    internal JObject ToJObject()
    {
        var serializer = JsonSerializer.Create(jsonSettings);
        return JObject.FromObject(this, serializer);
    }
}
