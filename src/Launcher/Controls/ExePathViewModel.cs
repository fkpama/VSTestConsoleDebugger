using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Launcher.ViewModels;

namespace Launcher.Controls
{
    public class ExePathViewModel : ViewModel
    {

        #region FullPath property

        private string? m_FullPath;
        /// <summary>
        /// FullPath property
        /// <summary>
        public string? FullPath
        {
            get => m_FullPath;
            set => this.SetProperty(ref m_FullPath, value);
        }

        #endregion FullPath property

        #region DisplayText property

        private string? m_DisplayText;
        private EntryViewModel vm;
        private string targetPath;

        public ExePathViewModel(EntryViewModel vm, string targetPath)
        {
            this.vm = vm;
            this.targetPath = targetPath;
        }

        /// <summary>
        /// DisplayText property
        /// <summary>
        public string? DisplayText
        {
            get => m_DisplayText;
            set => this.SetProperty(ref m_DisplayText, value);
        }

        #endregion DisplayText property
    }

    public sealed class ExePathConverter : IValueConverter
    {
        public object? Convert(object value,
                              Type targetType,
                              object parameter,
                              CultureInfo culture)
        {
            if (value == DependencyProperty.UnsetValue || value == null
                || value is not TextBlock tb)
                return Binding.DoNothing;

            if (tb.DataContext is not EntryViewModel vm)
            {
                return Binding.DoNothing;
            }

            if (tb.ActualWidth == 0)
            {
                if (vm is not null) vm.DisplayText = null;
                return Binding.DoNothing;
            }

            if (vm.Type != ProjectSelectorAction.Executable)
            {
                return vm.TargetPath;
            }

            var text = vm.TargetPath;
            var width = getTextWidth(tb, text, culture);
            if (width > tb.ActualWidth)
            {
                Debug.Assert(text[1] == ':' && text[2] == '\\');

                var letterSize = width / text.Length;
                var baseText = getTextWidth(tb, text.Substring(0, 3), culture);
                if (baseText > tb.ActualHeight)
                {
                    return Binding.DoNothing;
                }
                var drivePlusDots = baseText * 2;

                var maxRemainingLetters = Math.Max(0, Math.Truncate((getAvailableWidth(tb) - drivePlusDots) / letterSize));
                if (maxRemainingLetters == 0)
                {
                    return Binding.DoNothing;
                }
                var availableRemainingLetters = Math.Truncate((double)text.Length - (3 * 2));

                var remaining = Math.Min(availableRemainingLetters, maxRemainingLetters);
                var start = text.Length - (int)remaining;
                var idx = text.IndexOf(Path.DirectorySeparatorChar, start);
                if (idx > 0)
                {
                    start = idx;
                }
                start++;
                var str = text.Substring(start);

                var computedText = $"F:\\...\\{str}";
                vm.DisplayText = computedText;
            }

            return vm.DisplayText;
        }

        private double getAvailableWidth(TextBlock tb)
            => tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;

        static double getTextWidth(TextBlock tb, string path, CultureInfo culture)
        {
            var tp = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
            var fmt = new FormattedText(path,
                                    culture,
                                    FlowDirection.LeftToRight,
                                    tp,
                                    tb.FontSize,
                                    tb.Foreground,
                                    dpi);
            return fmt.Width;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
