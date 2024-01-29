using System.IO.Packaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using Launcher.Controls;
using Launcher.Models;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;

namespace Launcher
{
    internal static class Utils
    {
        readonly static Lazy<bool> lazyIsVsIde = new(() => !Environment.GetEnvironmentVariable("LAUNCHER_TESTHOST").IsPresent());
        private static string? s_vsTestConsole;


        internal static bool IsVsIde
        {
            [DebuggerStepThrough, MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => lazyIsVsIde.Value;
        }
        public static bool IsTestPlatformPackage(string name)
        {
            return name.EqualsOrdI(Constants.TestPlatformPackageName);
        }
        internal static string GetRuleFilePath(string v)
            => GetVsixFilePath($"Rules\\{v.Trim()}");
        internal static string GetVsixFilePath(string v)
        {
            var path = typeof(RuleProvider).Assembly.Location;
            path = Path.GetDirectoryName(path);
            var ret = Path.Combine(path, v);
            Debug.Assert(File.Exists(ret));
            return ret;
        }

        internal static bool GetAutoDetectSettingsFile(this ILaunchProfile profile)
        {
            return profile.OtherSettings?.GetBool(Constants.ProfileParams.SettingsFilePath)
                ?? false;
        }
        internal static string? GetSettingsFilePath(this ILaunchProfile profile)
        {
            return profile.OtherSettings?.GetString(Constants.ProfileParams.SettingsFilePath);
        }
        internal static bool IsEmptyVsTestConsole(ILaunchProfile profile)
            => IsEmptyVsTestConsole(profile, false);

        internal static bool IsVsTestConsole(this ILaunchProfile profile)
            => string.Equals(profile.CommandName, Constants.ProfileCommandName, StringComparison.OrdinalIgnoreCase);
        internal static bool IsEmptyVsTestConsole(ILaunchProfile profile, bool autoDetect)
        {
            return profile.IsVsTestConsole()
                && profile.ExecutablePath.IsMissing()
                && profile.WorkingDirectory.IsMissing()
                && !(profile.EnvironmentVariables?.Count > 0)
                && profile.GetAutoDetectSettingsFile() == autoDetect
                && profile.GetSettingsFilePath().IsMissing();
        }

        internal static bool HasEmptyVsTestLaunchProfile(this ILaunchSettingsProvider2 provider)
        {
            try
            {
                return provider.CurrentSnapshot?.HasEmptyVsTestLaunchProfile() ?? false;
            }
            catch (Exception ex)
            when(ex.GetType().FullName.EqualsOrd("Microsoft.VisualStudion.InternalException"))
            {
                return false;
            }
        }
        internal static bool HasEmptyVsTestLaunchProfile(this ILaunchSettings settings)
        {
            return settings.Profiles.Any(IsEmptyVsTestConsole);
        }

        internal static string FindVsTestConsole()
        {
            if (s_vsTestConsole.IsPresent())
            {
                Assumes.NotNullOrWhitespace(s_vsTestConsole);
                return s_vsTestConsole;
            }
            var asm = Assembly.GetEntryAssembly()?.GetDirectory();
            if (asm.IsMissing())
            {
                var shell  = ServiceProvider.GlobalProvider
                    .GetService<SVsShell, IVsShell>();
                asm = shell.GetStartupDir();
                if (asm.IsMissing())
                {
                    // TODO: TNI
                }
            }

            var path = Path.Combine(asm, Constants.DefaultVsTestConsoleExeRelativeLocation);

            if (!File.Exists(path))
            {
                // TODO
                throw new NotImplementedException();
            }

            s_vsTestConsole = path;
            return path;
        }

        [DebuggerStepThrough, MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int RequireOk(this int hr)
        {
            ErrorHandler.ThrowOnFailure(hr);
            return hr;
        }

        internal static void AddVsResources()
        {
            if (IsVsIde)
            {
                var name = typeof(DialogWindow).Assembly.GetName().Name;
                var dict = $"{PackUriHelper.UriSchemePack}://application:,,,/{name};component/Controls/VSResources.xaml";
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(dict) });
            }
        }


        internal static IEnumerable<IVsProject> GetAllValidProjectTargets(this IVsSolution solution,
            IVsProject? testAdapterProject = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Guid projectId = Guid.Empty;
            foreach(var project in solution.GetAllProjects(__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION))
            {
                if (!project.IsBuildable())
                    continue;

                if (testAdapterProject is not null)
                {
                    if (projectId == Guid.Empty)
                    {
                        projectId = testAdapterProject.GetProjectGuid();
                    }

                    var curId = project.GetProjectGuid();
                    if (projectId == curId)
                    {
                        continue;
                    }
                }

                yield return project;

            }
        }

        internal static void RemoveTarget(this IWritableLaunchProfile profile)
        {
            profile.OtherSettings?.Remove(Constants.ProfileParams.Target);
        }
        internal static void SetTarget(this IWritableLaunchProfile profile, string value)
        {
            if (value.IsMissing())
            {
                profile.RemoveTarget();
                return;
            }
            if (profile.OtherSettings is null)
                throw new InvalidOperationException();

            profile.OtherSettings[Constants.ProfileParams.Target] = value;
        }
        internal static object? GetTargetValue(this ILaunchProfile profile)
        {
            object? val = null; 
            profile.OtherSettings?.TryGetValue(Constants.ProfileParams.Target, out val);
            return val;
        }
        internal static string? GetTarget(this ILaunchProfile profile, ITargetSerializer serializer)
        {
            object? val = profile.GetTargetValue();
            return val switch
            {
                string s => serializer.IsValid(s) ? s : null,
                JObject jo => getTarget(TargetModel.Deserialize(jo)) ,
                IReadOnlyDictionary<string, object> dict => getTarget(TargetModel.Deserialize(dict)),
                _ => null
            };

            string? getTarget(TargetModel? model)
                => model?.IsValid == true
                    ? serializer.Serialize(model)
                    : null;
        }

        internal static ProjectSelectorAction? GetTargetType(this ILaunchProfile profile, ITargetSerializer serializer)
        {
            if(profile.OtherSettings?
                .TryGetValue(Constants.ProfileParams.Target, out var val) == true
                && val is string s)
            {
                return serializer.GetTargetType(s);
            }
            return null;
        }

        internal static Target GetEntry(this ILaunchProfile profile, ITargetSerializer serializer)
        {
            Debug.Assert(profile.IsVsTestConsole());
            var target = profile.GetTarget(serializer);
            if (target.IsMissing())
            {
                return Target.Empy;
            }
            Assumes.NotNull(target);
            return serializer.TryDeserializeEntry(target);
        }

        internal static IVsHierarchy? GetTargetProject(this IVsSolution solution,
                                                       IVsHierarchy adapterProjectHier,
                                                       Target target)
        {
            if (target.Mode != ProjectSelectorAction.Project)
                return null;

            if (target.Id.HasValue)
            {
                try
                {
                    return solution.GetProjectOfGuid(target.Id.Value);
                }
                catch(COMException) { }
            }

            if (target.TargetPath.IsPresent())
            {
                var tpath = target.TargetPath;
                if (!Path.IsPathRooted(tpath))
                {
                    tpath = Path.Combine(adapterProjectHier.GetProjectDirectory());
                }
                return solution.GetProjectOfUniqueName(tpath);
            }

            return null;
        }
    }
}
