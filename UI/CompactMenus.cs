using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Thexder.UI
{
    /// <summary>
    /// Tighter menus. WPF-UI's menu item templates fix their inner margins (6 px above and below every dropdown row,
    /// 10 px round the bar items), a 20 px check box and 14-point text, so an app-wide style on top of WPF-UI's trims
    /// them when each item loads and keeps the menu text at 12 points.
    /// </summary>
    internal static class CompactMenus
    {
        public static void Register(Application app)
        {
            if (app.TryFindResource(typeof(MenuItem)) is not Style wpfUi)
            {
                return;
            }
            var style = new Style(typeof(MenuItem), wpfUi);
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded)));
            app.Resources[typeof(MenuItem)] = style;
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Template == null)
            {
                return;
            }
            if (item.Role is MenuItemRole.TopLevelHeader or MenuItemRole.TopLevelItem)
            {
                // The bar: an outer border, then a grid holding the row (with its 10 px margin) and the dropdown.
                if (item.Template.FindName("Border", item) is Border border)
                {
                    border.Margin = new Thickness(2, 3, 2, 3);
                    if (border.Child is Panel outer && outer.Children.OfType<Grid>().FirstOrDefault() is { } inner)
                    {
                        inner.Margin = new Thickness(9, 4, 9, 4);
                    }
                    SetTextSize(border, item.FontSize);
                }
                return;
            }
            var row = item.Template.FindName("MenuItemContent", item) as FrameworkElement
                ?? (item.Template.FindName("Border", item) as Border)?.Child as FrameworkElement;
            if (row != null)
            {
                row.Margin = new Thickness(8, 3, 8, 3);
            }
            if (item.Template.FindName("Header", item) is ContentPresenter header)
            {
                SetTextSize(header, item.FontSize); // WPF-UI's header text is 14 points whatever the item's font size
            }
            if (item.Template.FindName("CheckBoxIconBorder", item) is FrameworkElement box)
            {
                box.Width = box.Height = 16;
            }
            if (item.Template.FindName("CheckBoxIcon", item) is TextBlock tick)
            {
                tick.FontSize = 13;
            }
        }

        private static void SetTextSize(DependencyObject parent, double size)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is AccessText access)
                {
                    access.FontSize = size;
                }
                else if (child is TextBlock text)
                {
                    text.FontSize = size;
                }
                SetTextSize(child, size);
            }
        }
    }
}
