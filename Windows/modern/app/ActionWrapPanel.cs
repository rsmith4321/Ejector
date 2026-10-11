using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace EasyEject.Modern {
    // Keep individually sized action labels reachable when the window narrows.
    public sealed class ActionWrapPanel : Panel {
        const double Gap = 12;
        protected override Size MeasureOverride(Size available) {
            double width = double.IsInfinity(available.Width) ? double.MaxValue : Math.Max(0, available.Width);
            double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
            foreach (var child in Children) {
                // Measure even collapsed children so XAML tracks their parent layout
                // dependency when a Stop/debug action becomes visible later.
                child.Measure(new Size(width, double.PositiveInfinity));
                if (child.Visibility == Visibility.Collapsed) continue;
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > width) { y += rowHeight + Gap; x = 0; rowHeight = 0; }
                usedWidth = Math.Max(usedWidth, x + size.Width); x += size.Width + Gap; rowHeight = Math.Max(rowHeight, size.Height);
            }
            return new Size(usedWidth, y + rowHeight);
        }
        protected override Size ArrangeOverride(Size finalSize) {
            double x = 0, y = 0, rowHeight = 0;
            foreach (var child in Children) {
                if (child.Visibility == Visibility.Collapsed) continue;
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > finalSize.Width) { y += rowHeight + Gap; x = 0; rowHeight = 0; }
                child.Arrange(new Rect(x, y, Math.Min(size.Width, finalSize.Width), size.Height));
                x += size.Width + Gap; rowHeight = Math.Max(rowHeight, size.Height);
            }
            return finalSize;
        }
    }
}
