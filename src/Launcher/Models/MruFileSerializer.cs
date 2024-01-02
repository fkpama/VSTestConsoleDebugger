using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json.Linq;
using Sodiware.IO;

namespace Launcher.Models
{
    [Export]
    [ProjectSystemContract(ProjectSystemContractScope.ConfiguredProject, ProjectSystemContractProvider.Extension)]
    [AppliesTo(Constants.VsTestConsoleCapability)]
    internal sealed class MruFileSerializer
    {
        private static char[] s_separator = new[] { Separator };
        private static char[] s_newLineSeparators = new[] { '\r', '\n' };
        public const char Separator = '|';
        private readonly ConfiguredProject project;

        [ImportingConstructor]
        public MruFileSerializer(ConfiguredProject project)
        {
            this.project = project;
        }

        public List<Target> Deserialize(StreamReader streamReader)
        {
            var str = streamReader.ReadToEnd().Split(s_newLineSeparators, StringSplitOptions.RemoveEmptyEntries);
            return str.Select(this.TryDeserializeEntry)
                .Where(x => x.TargetPath.IsPresent())
                .ToList();

        }
        public string Serialize(string filePath)
        {
            if (filePath.IsPresent() && Path.IsPathRooted(filePath))
            {
                filePath = this.project.UnconfiguredProject.MakeRelative(filePath);
            }
            return filePath;
        }

        public void Serialize(IEnumerable<Target> entries, TextWriter sw)
        {
            sw.Write(string.Join(Environment.NewLine, entries.Select(this.Serialize)));
        }
        public string Serialize(IEnumerable<Target> entries)
        {
            using var sw = new StringWriter();
            this.Serialize(entries, sw);
            return sw.ToString();
        }
        public string Serialize(Target x)
        {
            if (x.Mode == ProjectSelectorAction.Executable)
            {
                return Serialize(x.TargetPath);
            }
            Assumes.NotNull(x.Id);
            return this.Serialize(x.Mode, x.TargetPath, x.Id.Value);
        }

        public string? Serialize(TargetModel model)
            => model.EffectiveType switch
            {
                ProjectSelectorAction.Project
                => Serialize(ProjectSelectorAction.Project, model.Path!, Guid.Parse(model.ProjectId)),
                _ => model.Path
            };

        internal string Serialize(ProjectSelectorAction type,
                                  string filePath,
                                  Guid projectId,
                                  string? name = null)
        {
            if (Path.IsPathRooted(filePath))
            {
                filePath = this.project.UnconfiguredProject.MakeRelative(filePath);
            }
            if (type == ProjectSelectorAction.Executable)
            {
                return $"{type}|{filePath}";
            }

            return $"{type}|{projectId}|{filePath}|{name}";
        }


        public Target TryDeserializeEntry(string arg) => TryDeserializeEntry(arg, true);
        public Target TryDeserializeEntry(string arg, bool fullPath)
        {
            var idx = arg.Split(s_separator, 2);
            if (idx.Length == 1)
            {
                // It is a direct path
                if (!isValidExecutableEntry(idx[0], out var path))
                    return default;

                Assumes.NotNull(path);
                return new(ProjectSelectorAction.Executable, makePath(path, fullPath));
            }

            try
            {
                var mode = (ProjectSelectorAction)Enum.Parse(typeof(ProjectSelectorAction), idx[0]);
                if (mode == ProjectSelectorAction.Executable)
                {
                    if (isValidExecutableEntry(arg, out var path))
                    {
                        Assumes.NotNull(path);
                        return new(mode, path);
                    }
                }
                else
                {
                    var str2 = idx[1].Split('|');
                    if (str2[0].IsPresent() && Guid.TryParse(str2[0], out var projectId))
                    {
                        string name = string.Empty, filePath = str2[1];
                        if (str2.Length > 2)
                        {
                            name = str2[2];
                        }
                        return new(mode, filePath, projectId);
                    }
                }
            }
            catch (Exception ex)
            {
                // TODO: Log
            }
            return default;
        }

        private string makePath(string arg, bool fullPath)
        {
            if (!fullPath) return arg;
            var dir = Path.GetDirectoryName(this.project.UnconfiguredProject.FullPath)!;
            return Path.Combine(dir, arg);
        }

        private bool isValidExecutableEntry(string arg, [NotNullWhen(true)] out string? path)
        {
            if (arg.IsMissing())
            {
                path = null;
                return false;
            }
            var sp = arg.Split(s_separator, 2);
            path = sp.Length > 1 ? sp[1] : sp[0];
            return PathUtils.IsValidPath(path);
        }

        internal bool TryGetTargetType(string serializedValue, [NotNullWhen(true)] out ProjectSelectorAction? type)
        {
            var str = serializedValue.Split(s_separator, 2);
            if (str.Length == 1)
            {
                if (!PathUtils.IsValidPath(serializedValue))
                {
                    type = null;
                    return false;
                }
                type = ProjectSelectorAction.Executable;
                return true;
            }
            if (!Enum.TryParse<ProjectSelectorAction>(str[0], true, out var val))
            {
                type = null;
                return false;
            }
            type = val;
            return true;
        }
        internal ProjectSelectorAction GetTargetType(string serializedValue)
        {
            if (!TryGetTargetType(serializedValue, out var type))
            {
                throw new FormatException();
            }
            Assumes.NotNull(type);
            return type.Value;
        }

        internal bool IsValid(JObject jobject)
        {
            var model = TargetModel.Deserialize(jobject);
            return model?.IsValid == true;
        }
        internal bool IsValid(string s)
        {
            var sp = s.Split(s_separator);
            string path;
            if (sp.Length == 1 && (path = sp[0]).IsPresent()
                || sp.Length > 1
                && sp[0].IsPresent()
                && Enum.TryParse(sp[0], out ProjectSelectorAction action)
                && action == ProjectSelectorAction.Executable
                && (path = sp[1]).IsPresent())
            {
                return PathUtils.IsValidPath(path);
            }
            else if (sp.Length > 1
                && sp[0].IsPresent()
                && Enum.TryParse(sp[0], out action)
                && action == ProjectSelectorAction.Project
                && sp[1].IsPresent()
                && Guid.TryParse(sp[1], out _))
            {
                return true;
            }

            return false;
        }
    }
}
