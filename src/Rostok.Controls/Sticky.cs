using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rostok.Controls;

// Закреплённая боковая панель (position: sticky): пока колонка прокручивается, панель держится у верхнего края окна.
public static class Sticky
{
    public static T Attach<T>(T element, double top = 16) where T : FrameworkElement
    {
        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        element.VerticalAlignment = VerticalAlignment.Top;
        ScrollViewer? scroll = null;

        void Update()
        {
            if (scroll is null || element.Parent is not FrameworkElement column || !element.IsVisible) return;
            try
            {
                var colTop = column.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
                var free = column.ActualHeight - element.ActualHeight;
                shift.Y = Math.Max(0, Math.Min(free, top - colTop));
            }
            catch (InvalidOperationException) { /* элемент уже вне дерева */ }
        }

        void OnScroll(object s, ScrollChangedEventArgs e) => Update();
        element.Loaded += (_, _) =>
        {
            DependencyObject? p = element;
            while (p is not null && p is not ScrollViewer { VerticalScrollBarVisibility: not ScrollBarVisibility.Disabled }) p = VisualTreeHelper.GetParent(p);
            scroll = p as ScrollViewer;
            if (scroll is not null) scroll.ScrollChanged += OnScroll;
            Update();
        };
        element.Unloaded += (_, _) => { if (scroll is not null) scroll.ScrollChanged -= OnScroll; };
        element.SizeChanged += (_, _) => Update();
        return element;
    }
}
