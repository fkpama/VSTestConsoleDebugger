using System.Threading.Tasks.Dataflow;
using Launcher.Debugger;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.References;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using VSLangProj;
using static Sodiware.VisualStudio.VSConstantsEx;

namespace Launcher;

[Export(ExportContractNames.Scopes.ConfiguredProject, typeof(IProjectCapabilitiesProvider))]
[AppliesTo(ProjectCapabilities.Cps)]
internal class VsTestConsoleCapabilityProvider : ConfiguredProjectCapabilitiesProviderBase
{
    private readonly object sync = new();
    private bool previousResult = false;
    private readonly ConfiguredProject project;
    private readonly IPackageReferencesService2 outputGroups;
    private ImmutableHashSet<string> capabilities;
    private readonly ActionBlock<IComparable>? firstLink;
    private readonly IDisposable? subscription;

    [ImportingConstructor]
    public VsTestConsoleCapabilityProvider(ConfiguredProject project,
                                           IPackageReferencesService outputGroups)
        : base(nameof(VsTestConsoleCapabilityProvider), project)
    {
        this.project = project;
        this.outputGroups = (IPackageReferencesService2)outputGroups;
        this.capabilities = Empty.CapabilitiesSet;
    }

    protected async Task<bool> GetIsApplicableAsync(CancellationToken cancellationToken)
    {

        var properties = this.project.Services.ProjectPropertiesProvider
            ?.GetCommonProperties();
        if (properties is null)
        {
            return false;
        }

        var hasPackageTask = this.outputGroups
            .GetUnresolvedReferenceAsync(Constants.TestPlatformPackageName);
        var t1 = properties.GetUnevaluatedPropertyValueAsync(MSBuildProperties.IsTestProject)
            .WithCancellationToken(cancellationToken);
        var t2 = properties.GetUnevaluatedPropertyValueAsync(MSBuildProperties.TestProject)
            .WithCancellationToken(cancellationToken);
        var t3 = properties.GetEvaluatedPropertyValueAsync(MSBuildProperties.AssemblyName)
            .WithCancellationToken(cancellationToken);

        await Task.WhenAll(t1, t2, t3, hasPackageTask).NoAwait();
        var isTestProject = t1.Result.IsPresent() && XmlUtil.ToBoolean(t1.Result);
        var testProject = t2.Result.IsPresent() && XmlUtil.ToBoolean(t2.Result);
        var assemblyName = t3.Result;
        var hasPackage = hasPackageTask.Result is not null;

        var result = !isTestProject &&
            !testProject &&
            hasPackage &&
            assemblyName.EndsWithOI(Constants.TestAdapterFileExtension);
        if (previousResult != result)
        {
            if (result)
                Log.LogInformation($"Found MSTest Adapter project {Path.GetFileNameWithoutExtension(this.project.UnconfiguredProject.FullPath)}");
            previousResult = result;
        }
        return result;
            
    }

    protected override async Task<ImmutableHashSet<string>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var wasApplicable = this.capabilities.Count > 0;

        var isApplicable = await GetIsApplicableAsync(cancellationToken).NoAwait();
        if (isApplicable != wasApplicable)
        {
            if (isApplicable)
            {
                this.capabilities = ImmutableHashSet.Create(Constants.VsTestConsoleCapability);
            }
            else
            {
                this.capabilities = Empty.CapabilitiesSet;
            }
        }

        return this.capabilities;
    }
}
