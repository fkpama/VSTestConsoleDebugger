using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Dataflow;
using System.Windows.Markup;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Interop;

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
        private ActionBlock<ILaunchSettings>? actionBlock;
        private IDisposable? subscription;

        [ImportingConstructor]
        public RuleProvider(ConfiguredProject project,
                            IAdditionalRuleDefinitionsService additionalRule,
                            SVsServiceProvider services,
                            IProjectThreadingService threading,
                            ILaunchSettingsProvider3 launchSettingsProvider)
        {
            this.project = project;
            this.additionalRule = additionalRule;
            this.launchSettingsProvider = launchSettingsProvider;

            threading.JoinableTaskFactory.StartOnIdle(async () =>
            {
                ILaunchSettings? settings;
                settings = await launchSettingsProvider
                    .WaitForFirstSnapshot((int)TimeSpan.FromSeconds(30).TotalMilliseconds)
                    .NoAwait();
                if (settings is not null)
                {
                    createLink();
                    onProfileUpdated(settings);
                }
                else
                {
                    await threading.SwitchToUIThread();
                    services.GetSolution()
                    .GetProperty((int)__VSPROPID4.VSPROPID_IsSolutionFullyLoaded, out var pvar)
                    .RequireOk();
                    var loaded = Convert.ToBoolean(pvar);
                    if (loaded)
                        onAfterBackgroundSolutionLoadComplete(null, null);
                    else
                        SolutionEvents.OnAfterBackgroundSolutionLoadComplete += onAfterBackgroundSolutionLoadComplete;
                }
            }).FileAndForget();
        }

        private void onProfileUpdated(ILaunchSettings value)
        {
            var hasCapa = this.project.Capabilities.Contains(Constants.VsTestConsoleCapability);
            if (!hasCapa || value.HasEmptyVsTestLaunchProfile())
            {
                this.removeGenericDebugger();
            }
            else if (hasCapa)
            {
                this.addGenericDebugger();
            }
        }

        private void createLink()
        {
            if (this.actionBlock is null)
                launchSettingsProvider.SourceBlock.LinkTo(actionBlock = new(onProfileUpdated));
        }
        private void onAfterBackgroundSolutionLoadComplete(object sender, EventArgs e)
        {
            SolutionEvents.OnAfterBackgroundSolutionLoadComplete -= onAfterBackgroundSolutionLoadComplete;
            createLink();
            if (!this.launchSettingsProvider.HasEmptyVsTestLaunchProfile())
            {
                this.addGenericDebugger();
            }
        }

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
            this.actionBlock?.Complete();
            this.subscription?.Dispose();
        }
    }
}
