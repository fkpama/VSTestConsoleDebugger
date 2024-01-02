using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sodiware.VisualStudio.ProjectSystem.PropertyPages;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
        public required TestContext TestContext { get; set; }

        public CancellationToken cancellationToken => TestContext.CancellationTokenSource.Token;

        static UnitTest1()
        {
            const string path = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\Microsoft\ManagedProjectSystem";
            string dllPath = Path.Combine(path, @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\Microsoft\ManagedProjectSystem\Microsoft.VisualStudio.ProjectSystem.Managed.dll");
            Assembly.LoadFrom(dllPath);
            AppDomain.CurrentDomain.AssemblyResolve += (o, e) =>
            {
                var asm = new AssemblyName(e.Name).Name;
                dllPath = Path.Combine(path, $"{asm}.dll");
                if (File.Exists(dllPath))
                {
                    return Assembly.LoadFrom(dllPath);
                }
                return null;
            };
        }

        [TestMethod]
        public async Task TestMethod1()
        {
            var env = Environment.GetEnvironmentVariable("VSAPPIDDIR");
            //env = Path.Combine(env, Constants.DefaultVsTestConsoleExeRelativeLocation);
            //var output = new Mock<IOutputGroupsService2>();
            //using var launcher = new DebugLauncherHelper(output.Object, null);

            //mock.Setup(x => x.LaunchAsync(It.IsAny<DebugLaunchSettings>(),
            //    It.IsAny<CancellationToken>()))
            //    .Verifiable();

            //await launcher.LaunchAsync(0, cancellationToken);

            //mock.VerifyAll();
        }

        [TestMethod]
        public void MyTestMethod()
        {
            const string text = """""
                {
                    "prop1": {
                        "prop1_1": true
                    },
                    "prop2": "value2"
                }
                """"";
            const string text2 = @"Value1=Hel/,lo,Value2=World";
            var json = LaunchProfileEnvironmentVariableEncoding.ParseIntoDictionary(text2);
        }
    }
}