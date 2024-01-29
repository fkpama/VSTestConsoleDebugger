using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Dataflow;
using System.Windows.Markup;
using Launcher.Debugger;
using Launcher.Models;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Launcher
{
    [AppliesTo(Constants.VsTestConsoleCapability)]
    [ExportRuleObjectProvider(name: Constants.DebuggerName, context: PropertyPageContexts.Project)]
    [Order(9999999)]
    internal sealed class RuleProvider : IRuleObjectProvider, IDisposable
    {
        readonly static string[] s_rules = new[] { "Rule.xml", };
        private Rule? debuggerRule;
        [MemberNotNullWhen(true, nameof(debuggerRule))]
        private bool Registerd { get; set; }

        private readonly ConfiguredProject project;
        private readonly IAdditionalRuleDefinitionsService additionalRule;
        private readonly ILaunchSettingsProvider2 launchSettingsProvider;
        private readonly IDebugLaunchCommandHook launchCommandHook;
        private ActionBlock<ILaunchSettings>? actionBlock;
        private readonly Lazy<MsTestProject> msProject;
        private IDisposable? subscription;
        private bool solutionClosed;

        [ImportingConstructor]
        public RuleProvider(ConfiguredProject project,
                            IAdditionalRuleDefinitionsService additionalRule,
                            SVsServiceProvider services,
                            IProjectThreadingService threading,
                            [ImportMany(ExportContractNames.VsTypes.IVsProject)]
                            IEnumerable<Lazy<IVsProject, IOrderPrecedenceMetadataView>> vsProjects,
                            ITargetSerializer serializer,
                            IDebugLaunchCommandHook launchCommandHook,
                            ILaunchSettingsProvider3 launchSettingsProvider)
        {
            this.project = project;
            this.additionalRule = additionalRule;
            this.launchSettingsProvider = launchSettingsProvider;
            this.launchCommandHook = launchCommandHook;
            this.msProject = new(() =>
            {
                var vsProj = vsProjects.ToImportCollection(project).First().Value;
                var msTestProject = new MsTestProject(vsProj,
                                                      serializer,
                                                      services.GetLazyService<SVsSolution, IVsSolution>());
                this.launchCommandHook.Add(msTestProject);
                return msTestProject;
            });

            SolutionEvents.OnAfterBackgroundSolutionLoadComplete += initialize;
            SolutionEvents.OnBeforeCloseSolution += onBeforeCloseSolution;
            SolutionEvents.OnAfterCloseSolution += onAfterCloseSolution;
        }

        private void unregisterEvents()
        {
            //SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= onAfterBackgroundSolutionLoadComplete;
            SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= initialize;
            SolutionEvents.OnBeforeCloseSolution -= onBeforeCloseSolution;
            SolutionEvents.OnAfterCloseSolution -= onAfterCloseSolution;
        }
        private void initialize(object sender, EventArgs e)
        {
            SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= initialize;
            if (this.solutionClosed) return;
            Task.Run(async () =>
            {
                if (this.solutionClosed)
                    return;
                await TaskScheduler.Default;
                ILaunchSettings? settings;
                while (true)
                {
                    try
                    {
                        settings = await launchSettingsProvider
                            .WaitForFirstSnapshot((int)TimeSpan.FromSeconds(10).TotalMilliseconds)
                            .NoAwait();
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
                if (settings is not null)
                {
                    createLink();
                    onProfileUpdated(settings);
                }
            }).FileAndForget();
        }

        private void onProfileUpdated(ILaunchSettings value)
        {
            if (this.solutionClosed) return;
            var hasCapa = this.project.Capabilities.Contains(Constants.VsTestConsoleCapability);
            if (!hasCapa || value.HasEmptyVsTestLaunchProfile())
            {
                this.removeGenericDebugger();
            }
            else if (hasCapa)
            {
                this.addGenericDebugger();
            }

            if (!hasCapa)
            {
                if (this.msProject.IsValueCreated)
                {
                    this.msProject.Value.OnProfileUpdated(null);
                    this.launchCommandHook.Remove(this.msProject.Value);
                }
            }
            else
            {
                this.msProject.Value.OnProfileUpdated(value);
                this.launchCommandHook.Add(this.msProject.Value);
            }
        }

        private void createLink()
        {
            if (!this.solutionClosed && this.actionBlock is null)
            {
                this.subscription = launchSettingsProvider.SourceBlock.LinkTo(actionBlock = new(onProfileUpdated));
            }
        }

        private void onBeforeCloseSolution(object sender, EventArgs e)
        {
            this.solutionClosed = true;
            SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= initialize;
            SolutionEvents.OnBeforeCloseSolution -= onBeforeCloseSolution;
            this.removeProject();
            this.actionBlock?.Complete();
            this.actionBlock = null;
        }

        private void removeProject()
        {
            if (this.msProject.IsValueCreated)
                this.launchCommandHook.Remove(this.msProject.Value);
        }

        private void onAfterCloseSolution(object sender, EventArgs e)
        {
            SolutionEvents.OnAfterCloseSolution -= onAfterCloseSolution;
            this.Dispose();
            this.subscription = null;
        }

        //private void onAfterBackgroundSolutionLoadComplete(object sender, EventArgs e)
        //{
        //    SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= onAfterBackgroundSolutionLoadComplete;
        //    if (this.solutionClosed) return;
        //    createLink();
        //    if (!this.launchSettingsProvider.HasEmptyVsTestLaunchProfile())
        //    {
        //        this.addGenericDebugger();
        //    }
        //}

        private void addGenericDebugger()
        {
            if (!this.Registerd)
            {
                if (launchSettingsProvider.CurrentSnapshot?.HasEmptyVsTestLaunchProfile() == true)
                {
                    return;
                }
                var activeProfile = this.launchSettingsProvider.CurrentSnapshot?.ActiveProfile;
                var rule = createDebuggerRule();
                this.additionalRule.AddRuleDefinition(rule, PropertyPageContexts.Project);
                this.Registerd = true;
                if (activeProfile?.Name.IsPresent() == true
                    && activeProfile != this.launchSettingsProvider.CurrentSnapshot?.ActiveProfile)
                {
                    Assumes.NotNull(activeProfile.Name);
                    this.launchSettingsProvider
                        .SetActiveProfileAsync(activeProfile.Name)
                        .FileAndForget();
                }
            }
        }

        private void removeGenericDebugger()
        {
            if (this.Registerd)
            {
                this.additionalRule.RemoveRuleDefinition(this.debuggerRule);
                this.Registerd = false;
            }
        }

        [MemberNotNull(nameof(debuggerRule))]
        private Rule createDebuggerRule()
        {
            if (this.debuggerRule is not null)
                return this.debuggerRule;
            lock (s_rules)
            {
                var rule = new Rule();
                rule.BeginInit();
                rule.Name = Constants.GenericDebuggerName;
                rule.DisplayName = "VSTest Console";
                rule.PageTemplate = "debugger";
                rule.Description = "Launch vstest.console.exe";
                rule.ShowOnlyRuleProperties = false;
                rule.DataSource = new()
                {
                    Persistence = "UserFile",
                    SourceOfDefaultValue = DefaultValueSourceLocation.AfterContext
                };

                rule.Categories.Add(new() { Name = "General" });

                (rule.Properties ??= new()).Add(new StringProperty
                {
                    Name = Constants.GenericDebuggerProperties.TargetPath,
                    DisplayName = "My Rule",
                    Category = "General"
                    //DataSource = new()
                    //{
                    //    Persistence = "UserFile",
                    //    SourceOfDefaultValue= DefaultValueSourceLocation.AfterContext
                    //}
                });

                rule.EndInit();
                this.debuggerRule = rule;
                return rule;
            }
        }

        public IReadOnlyCollection<Rule> GetRules()
        {
            var lst = new List<Rule>();
            foreach(var filename in s_rules)
            {
                var path = Utils.GetRuleFilePath(filename);
                if (!File.Exists(path))
                {
                    Debug.Fail($"Missing file {path}");
                    // TODO: Log
                    return Array.Empty<Rule>();
                }
                using var rdr = File.OpenRead(path);
                var ruleSet = (Rule)XamlReader.Load(rdr);
                lst.Add(ruleSet);

            }
            return lst.ToImmutableList();
        }

        public void Dispose()
        {
            this.removeProject();
            this.unregisterEvents();
            this.actionBlock?.Complete();
            this.actionBlock = null;
            this.subscription?.Dispose();
            this.subscription = null;
        }
    }
}
