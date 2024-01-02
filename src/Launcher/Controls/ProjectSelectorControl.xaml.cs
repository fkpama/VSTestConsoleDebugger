using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Launcher.ViewModels;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Win32;

namespace Launcher.Controls
{
    /// <summary>
    /// Interaction logic for ProjectSelectorControl.xaml
    /// </summary>
    public partial class ProjectSelectorControl : UserControl
    {
        public ProjectSelectorControl()
        {
            InitializeComponent();
        }

        private void OnProjectMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var vm = this.DataContext as ProjectSelectorViewModel;
            if (vm?.HandleProjectDoubleClick() == true)
            {
                this.FindAncestor<Window>().Close();
            }
        }

        private void OpenFileDialog(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = true,
                Filter = "Binary files (*.dll)|*.dll|All Files (*.*)|*.*",
            };
            var result = dialog.ShowDialog();
            if (result.GetValueOrDefault())
            {
                var vm = this.DataContext as ProjectSelectorViewModel;
                if (vm is not null)
                    vm.SelectedExecutable = dialog.FileName;
            }

        }

        private void ExecutableFilter(object sender, System.Windows.Data.FilterEventArgs e)
        {
            e.Accepted = ((EntryViewModel)e.Item).Type == ProjectSelectorAction.Executable;
        }
    }
}
