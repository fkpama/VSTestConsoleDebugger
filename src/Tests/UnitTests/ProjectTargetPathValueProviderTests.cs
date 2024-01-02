using System.Collections.Immutable;
using System.Windows.Markup;
using Launcher.PropertyPages;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.Shell.Interop;
using Constants = Launcher.Constants;
using static Launcher.Constants;
using static Launcher.PropertyPages.TargetValueProvider;
using Sodiware;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Package;
using System.Reflection;
using Launcher.Models;

namespace UnitTests
{
    [TestClass]
    public class ProjectTargetPathValueProviderTests
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static ImmutableDictionary<string, object> globalSettings = ImmutableDictionary<string, object>.Empty;
        private Mock<ConfiguredProject> configuredProject;
        private MruFileSerializer serializer;
        private Mock<IProjectThreadingService> threadingService;
        private Mock<IServiceProvider> services;
        private Mock<ITargetValueProviderHelper> mockHelper;
        private Mock<ILaunchProfile> mockProfile;
        private Dictionary<string, object> otherProperties;
        private Rule rule;

        private List<Lazy<IVsProject, IOrderPrecedenceMetadataView>> vsProjects;
        TargetValueProvider sut;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private static string DefaultProjectPath = Path.Combine(
            MethodBase.GetCurrentMethod().DeclaringType.Assembly.GetDirectory(),
            "Project1.csproj");
        [TestInitialize]
        public void TestInitialize()
        {
            configuredProject = new Mock<ConfiguredProject>();
            serializer = new(configuredProject.Object);
            threadingService = new();
            services = new();
            vsProjects = new();
            otherProperties = new();

            mockProfile = new();
            var mockWritable = mockProfile.As<IWritableLaunchProfile>();
            mockWritable.Setup(x => x.OtherSettings).Returns(() => otherProperties);
            mockProfile.Setup(x => x.OtherSettings).Returns(() => otherProperties.ToImmutableDictionary());

            mockHelper = new();
            mockHelper.Setup(x => x.ProjectDirectory).Returns(Environment.CurrentDirectory);

            var path = Launcher.Utils.GetRuleFilePath("Rule.xml");
            using var stream = File.OpenRead(path);
            rule = (Rule)XamlReader.Load(stream);

            sut = new(mockHelper.Object, serializer);
        }

        [TestMethod]
        public void ProjectTargetPathValueProvider_Can_initialize()
        {
            getValues(out var action, out var project, out var exe);
            Assert.AreEqual(action, ProjectSelectorAction.Executable);
            Assert.IsNull(project);
            Assert.AreEqual(string.Empty, exe);
        }

        [TestMethod]
        public void ProjectTargetPathValueProvider_can_set_executable()
        {
            const string executable = "Hello WOrld";
            setExecutable(executable);
            getValues(out var action, out var project, out var exe);
            check(ProjectSelectorAction.Executable, null, executable);
        }

        [TestMethod]
        public void ProjectTargetPathValueProvider_can_set_project()
        {
            var projectId = Guid.NewGuid();
            addProject(projectId);
            setProject(projectId);
            getValues(out var action, out var project, out var exe);
            Assert.AreEqual(action, ProjectSelectorAction.Project);
            Assert.AreEqual(projectId, project);
            Assert.AreEqual(string.Empty, exe);
        }

        [TestMethod]
        public void ProjectTargetPathValueProvider_returns_null_when_target_type_change_to_exe()
        {
            var projectId = Guid.NewGuid();
            var expected = "Some_exe";
            addProject(projectId);
            setProject(projectId);
            setTarget(ProjectSelectorAction.Executable);
            check(ProjectSelectorAction.Executable, null, null);
        }

        [TestMethod]
        public void ProjectTargetPathValueProvider_returns_null_when_target_type_changed()
        {
            var projectId = Guid.NewGuid();
            var expected = "Some_exe";
            setExecutable(expected);
            check(ProjectSelectorAction.Executable, null, expected);
            setTarget(ProjectSelectorAction.Project);
            check(ProjectSelectorAction.Project, null, null);
        }

        [TestMethod]
        public void ProjectTargetPathValueProvider_returns_null_project_when_changed()
        {
            var projectId = Guid.NewGuid();
            var expected = "Some_exe";
            addProject(projectId);
            setProject(projectId);

            setExecutable(expected);
            getValues(out var action, out var project, out var exe);
            Assert.AreEqual(action, ProjectSelectorAction.Executable);
            Assert.AreEqual(null, project);
            Assert.AreEqual(expected, exe);
        }

        private void addProject(Guid projectId,
                                string? filePath = null,
                                string? name = null,
                                IVsProject? project = null)
        {
            filePath ??= DefaultProjectPath;
            name ??= Path.GetFileNameWithoutExtension(filePath);
            project ??= new Mock<IVsProject>().Object;
            mockHelper.Setup(x => x.GetCachedProject(It.Is<Guid>(x => x == projectId)))
                .Returns(new TargetValueProviderHelper.VsProjectInfos
                {
                    FilePath = filePath,
                    Id = projectId,
                    Name = name,
                    Project = project
                });
        }

        private void setTarget(ProjectSelectorAction project)
            => set(ProfileParams.TargetType, project.ToString());

        private void setProject(Guid projectId)
            => setProject(projectId.ToString());
        private void setProject(string executable)
            => set(ProfileParams.ProjectTarget, executable);
        private void set(string property, string value)
        {
            this.sut.OnSetPropertyValue(property,
                                        value,
                                        (IWritableLaunchProfile)mockProfile.Object,
                                        globalSettings,
                                        rule);
        }
        private void setExecutable(string executable)
            => set(ProfileParams.ExeTarget, executable);

        private void getValues(out ProjectSelectorAction action,
            out Guid? projectTarget,
            out string exeTarget)
        {
            var value = sut.OnGetPropertyValue(Constants.ProfileParams.TargetType,
                                   mockProfile.Object,
                                   globalSettings,
                                   rule);
            action = (ProjectSelectorAction)Enum.Parse(typeof(ProjectSelectorAction), value);

            var target = sut.OnGetPropertyValue(Constants.ProfileParams.ProjectTarget,
                mockProfile.Object,
                globalSettings,
                rule);

            if (target.IsPresent()) projectTarget = Guid.Parse(target);
            else projectTarget = null;

            exeTarget = sut.OnGetPropertyValue(Constants.ProfileParams.ExeTarget,
                mockProfile.Object,
                globalSettings,
                rule);
        }

        void check(ProjectSelectorAction action,
                   Guid? projectId = null,
                   string? executable = null)
        {
            getValues(out var action1, out var project, out var exe);
            Assert.AreEqual(action, action1);
            Assert.AreEqual(projectId, project);
            executable ??= string.Empty;
            Assert.AreEqual(executable, exe);
        }
    }
}