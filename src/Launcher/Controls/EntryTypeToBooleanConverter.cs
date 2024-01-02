using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Launcher.ViewModels;

namespace Launcher.Controls
{
    public sealed class EntryConverter : IValueConverter
    {
        public object? Convert(object value,
                              Type targetType,
                              object parameter,
                              CultureInfo culture)
        {
            if (value == DependencyProperty.UnsetValue || value is null)
                return targetType == typeof(bool) ? false : null;

            if (targetType == typeof(bool) || targetType == typeof(bool?))
            {
                return convertEntryType(value, parameter, culture);
            }
            else if (targetType == typeof(string) && value is EntryViewModel vm)
            {
                return vm.TargetPath;
            }

            // TODO: Log
            return Binding.DoNothing;
        }

        private object convertEntryType(object value, object parameter, CultureInfo culture)
        {
            if (value is not ProjectSelectorAction val1
                || parameter is not ProjectSelectorAction val2)
            {
                return false;
            }

            return val1 == val2;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EntryViewModel vm)
            {
                if (targetType == typeof(string))
                    return vm.TargetPath;
                else if (targetType == typeof(ProjectSelectorAction))
                    return vm.Type;
            }
            return value?.Equals(true) == true ? parameter : Binding.DoNothing;
        }
    }
}
