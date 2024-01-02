using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace DummyTestAdapter
{
    [DefaultExecutorUri("my://test_executor/")
        , FileExtension(".dll")]
    public class TestDiscoverer : ITestDiscoverer
    {
        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            throw new NotImplementedException();
        }
    }
    [ExtensionUri("my://test_executor/")]
    public class Class1 : ITestExecutor
    {
        internal const string ExecutorUri = "my://test_executor/";
        void doDummyTests(IFrameworkHandle handle)
        {
            var testCase = new TestCase("Dummy.Test", new(ExecutorUri), "Source.cs");
            handle.RecordStart(testCase);
            Thread.Sleep(1000);
            handle.RecordResult(new(testCase)
            {
                Outcome = TestOutcome.Passed

            });
            Console.WriteLine("Hello ");
        }
        public void Cancel()
        {
            throw new NotImplementedException();
        }

        public void RunTests(IEnumerable<TestCase>? tests, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
        {
            doDummyTests(frameworkHandle);
        }

        public void RunTests(IEnumerable<string>? sources, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
        {
            doDummyTests(frameworkHandle); 
        }
    }
}