using Launcher.Models;
using Newtonsoft.Json.Linq;

namespace Launcher
{
    internal interface ITargetSerializer
    {
        List<Target> Deserialize(StreamReader streamReader);
        ProjectSelectorAction GetTargetType(string serializedValue);
        bool IsValid(JObject jobject);
        bool IsValid(string s);
        string Serialize(IEnumerable<Target> entries);
        void Serialize(IEnumerable<Target> entries, TextWriter sw);
        string Serialize(string filePath);
        string Serialize(Target x);
        string? Serialize(TargetModel model);
        Target TryDeserializeEntry(string arg);
        Target TryDeserializeEntry(string arg, bool fullPath);
    }
}