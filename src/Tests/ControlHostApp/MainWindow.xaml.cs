using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Launcher.Controls;
using Launcher.ViewModels;

namespace ControlHostApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ProjectSelectorViewModel viewModel;
        private MockProfileSaver profileSaver;
        public MainWindow()
        {
            this.profileSaver = new();
            var vm = new ProjectSelectorViewModel
            {
                LaunchProfileSaver = profileSaver
            };
            this.viewModel = vm;
            vm.Add(@"F:\Sources\Sodiware\VS\VSTestConsoleDebugger\src\Tests\ControlHostApp\bin\Debug\net472\MessagePack.dll");
            this.DataContext = vm;
            InitializeComponent();
            this.TestedContentConainer.DataContext = vm;
        }

        private void OnReloadComponent(object sender, RoutedEventArgs e)
        {
            var control = new ProjectSelectorControl();
            this.TestedContentConainer.Content = control;
        }
    }
}