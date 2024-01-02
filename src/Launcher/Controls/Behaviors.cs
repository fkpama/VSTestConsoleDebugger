using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Launcher.ViewModels;
using Microsoft.VisualStudio.PlatformUI;

namespace Launcher.Controls
{
    public static class Behaviors
    {
        #region UpdateTextOnSizeChanged

        private static DependencyProperty UpdateTextOnSizeChangedProperty
            = DependencyProperty.RegisterAttached("UpdateTextOnSizeChanged",
                typeof(bool),
                typeof(Behaviors),
                new FrameworkPropertyMetadata
                {
                    DefaultValue = false,
                    IsNotDataBindable = true,
                    PropertyChangedCallback = onExePathBehaviorChanged
                });
        private static void onExePathBehaviorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            doSetUpdateTextOnSizeChanged(tb, (bool)e.NewValue);
        }

        [AttachedPropertyBrowsableForType(typeof(TextBlock))]
        public static bool GetUpdateTextOnSizeChanged(this TextBlock textBlock)
            => (bool)textBlock.GetValue(UpdateTextOnSizeChangedProperty);

        public static void SetUpdateTextOnSizeChanged(this TextBlock textBlock, bool value)
        {
            var cur = GetUpdateTextOnSizeChanged(textBlock);
            if (cur == value)
            {
                return;
            }
            doSetUpdateTextOnSizeChanged(textBlock, value);
        }
        static void doSetUpdateTextOnSizeChanged(this TextBlock textBlock, bool value)
        {
            if (value)
            {
                textBlock.SizeChanged += onSizeChanged;
                if (!textBlock.IsInitialized)
                    textBlock.Initialized += onInitialized;
                else
                    onSizeChanged(textBlock);
            }
            else
            {
                textBlock.SizeChanged -= onSizeChanged;
                textBlock.Initialized -= onInitialized;
            }
        }

        private static void onInitialized(object sender, EventArgs e)
            => onSizeChanged((TextBlock)sender);

        private static void onSizeChanged(object sender, SizeChangedEventArgs e)
            => onSizeChanged((TextBlock)sender);
        private static void onSizeChanged(TextBlock tb)
        {
            var binding = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
            if (binding is null)
            {
                return;
            }

            binding.UpdateTarget();
        }

        #endregion UpdateTextOnSizeChanged

        #region EntrySelectionBehavior

        private static DependencyProperty EntrySelectionBehaviorProperty
            = DependencyProperty.RegisterAttached("EntrySelectionBehavior", typeof(bool),
                typeof(Behaviors),
                new FrameworkPropertyMetadata
                {
                    PropertyChangedCallback = (o, e) => SetEntrySelectionBehavior((Control)o, (bool)e.NewValue)
                });

        public static bool GetEntrySelectionBehavior(UIElement item)
            => (bool)item.GetValue(EntrySelectionBehaviorProperty);

        public static void SetEntrySelectionBehavior(Control item, bool value)
        {
            if (value != (bool)item.GetValue(EntrySelectionBehaviorProperty))
                return;
            if (value)
            {
                item.MouseDoubleClick += onMouseDoubleClick;
            }
            else
            {
                item.MouseDoubleClick -= onMouseDoubleClick;
            }
        }
        private static void onMouseDoubleClick(object sender, MouseButtonEventArgs e)
            => doUpdateEntrySelectionBehavior((Control)sender);


        private static void doUpdateEntrySelectionBehavior(Control item)
        {
            if(item.DataContext is not EntryViewModel vm)
            {
                return;
            }

            for(var cur = VisualTreeHelper.GetParent(item);
                cur is not null;
                cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is not FrameworkElement fe
                    || fe.DataContext is not ProjectSelectorViewModel selectorViewModel)
                {
                    continue;
                }

                if(selectorViewModel.SelectEntry(entry: vm) == ControlAction.CloseWindow
                    && Utils.IsVsIde)
                {
                    item.FindAncestor<Window>()?.Close();
                }
                break;
            }
        }

        #endregion
    }
}
