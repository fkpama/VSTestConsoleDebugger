using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using Launcher.Models;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using Newtonsoft.Json;
using Sodiware.IO;

namespace Launcher
{
    internal sealed class TestAdapterMruService
    {
        private readonly IServiceProvider serviceProvider;

        private List<Target>? mruList;
        private readonly object sync = new();
        private readonly Lazy<string> mruFilePath;
        private readonly MruFileSerializer serializer;

        internal string MruFilePath => mruFilePath.Value;

        public ConfiguredProject ConfiguredProject { get; }
        public MruFileSerializer Serializer => this.serializer;

        public TestAdapterMruService(ConfiguredProject project,
            MruFileSerializer serializer,
            IServiceProvider serviceProvider)
        {
            this.ConfiguredProject = project;
            this.serializer = serializer;
            this.serviceProvider = serviceProvider;
            this.mruFilePath = new(getMruFilePath);
        }

        internal Task PushEntryAsync(Target entry, CancellationToken cancellationToken)
        {
            if (this.mruList is null)
            {
            }
            var lst = this.mruList
                    .Distinct((x, y) => PathUtils.IsSamePath(x.TargetPath, y.TargetPath))
                    .ToList();
            tryRemoveEntry(lst, entry);
            lst.Insert(0, entry);
            lock (sync)
                this.mruList = lst;
            this.saveMruList();

            return Task.CompletedTask;
        }

        [DebuggerStepThrough, MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void tryRemoveEntry(List<Target> lst, Target entry)
        {
            var idx = lst.IndexOf(x => PathUtils.IsSamePath(x.TargetPath, entry.TargetPath));
            if (idx >= 0)
                lst.RemoveAt(idx);
        }

        private void saveMruList()
        {
            Assumes.NotNull(this.mruList);
            if (this.mruList.Count == 0)
            {
                PathUtils.Delete(this.MruFilePath);
            }
            else
            {
                using var sw = new StreamWriter(MruFilePath, false);
                sw.BaseStream.SetLength(0);
                this.serializer.Serialize(this.mruList, sw);
                sw.Flush();
                //File.WriteAllLines(MruFilePath, this.mruList.Select(serialize));
            }
        }

        [MemberNotNull(nameof(mruList))]
        private List<Target> getMruList()
        {
            if (this.mruList is not null)
                return this.mruList;
            var mruFilePath = MruFilePath;
            var lst = new List<Target>();
            if (File.Exists(mruFilePath))
            {
                var str = File.ReadAllLines(mruFilePath)
                    .Select(this.serializer.TryDeserializeEntry)
                    .Where(x => x.TargetPath.IsPresent());
                lst.AddRange(str);
            }
            this.mruList = lst;
            return this.mruList;
        }

        //private TargetSelectionResult tryDeserialize(string arg)
        //{
        //    var idx = arg.Split(s_separator, 2);
        //    if (idx.Length == 1)
        //    {
        //        return new(ProjectSelectorAction.Executable, arg);
        //    }

        //    try
        //    {
        //        var mode = (ProjectSelectorAction)Enum.Parse(typeof(ProjectSelectorAction), idx[0]);
        //        if (mode == ProjectSelectorAction.Executable)
        //        {
        //            return new(mode, idx[1]);
        //        }
        //        else
        //        {
        //            throw new NotImplementedException();
        //        }
        //    }
        //    catch(Exception ex)
        //    {
        //        // TODO: Log
        //    }
        //    return default;
        //}

        private string getMruFilePath()
        {
            var store = new ShellSettingsManager(this.serviceProvider);
            var folder = store.GetApplicationDataFolder(ApplicationDataFolder.LocalSettings);
            return Path.Combine(folder, "vstest.console.mru.json");
        }
        internal Task<Target[]> GetEntriesAsync(CancellationToken cancellationToken)
        {
            var lst = this.getMruList();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(lst.ToArray());
        }
    }
}
