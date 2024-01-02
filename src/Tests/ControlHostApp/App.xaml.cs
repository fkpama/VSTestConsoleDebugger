using System.Configuration;
using System.Data;
using System.IO.Packaging;
using System.Windows;
using Microsoft.VisualStudio.Imaging;

namespace ControlHostApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            Environment.SetEnvironmentVariable("LAUNCHER_TESTHOST", "1");
        }
    }
}