using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    // Source layouts use 96-DPI logical dimensions. Prepare once after the derived
    // constructor has assembled the complete layout, before a modal/window is shown.
    internal abstract class DpiForm : Form {
        bool prepared;
        // Tests only: lay out as the app would at this fixed display scale, whatever the live
        // monitor's scale is. The real-DPI layout runs first, then bounds and text are scaled by
        // the remaining ratio. Production leaves it null and always uses the real display DPI.
        internal static float? FixedScaleForTests { get; set; }
        internal void PrepareDpiLayout() {
            if (prepared) return;
            prepared = true;
            SuspendLayout();
            Size client = ClientSize, minimum = MinimumSize, maximum = MaximumSize;
            AutoScaleMode = AutoScaleMode.Dpi;
            SizeF current = CurrentAutoScaleDimensions;
            float? fixedScale = FixedScaleForTests;
            var real = new SizeF(current.Width / 96, current.Height / 96);
            var factor = fixedScale.HasValue ? new SizeF(fixedScale.Value, fixedScale.Value) : real;
            var columns = new List<KeyValuePair<ColumnHeader, int>>(); CaptureColumns(this, columns);
            AutoScaleDimensions = new SizeF(96, 96);
            ResumeLayout(false); PerformAutoScale();
            if (fixedScale.HasValue && real.Width > 0 && real.Height > 0) {
                var remaining = new SizeF(factor.Width / real.Width, factor.Height / real.Height);
                ScaleFonts(this, remaining.Height); Scale(remaining);
            }
            // Framework 4.8 bounds need an explicit, once-only logical baseline too.
            MinimumSize = ScaleBounds(minimum, factor); MaximumSize = ScaleBounds(maximum, factor);
            // Forms initialized through Size or with an early native handle do not
            // consistently rescale their viewport on Framework 4.8.
            ClientSize = ScaleBounds(client, factor);
            foreach (var column in columns) column.Key.Width = (int)Math.Round(column.Value * factor.Width);
            PerformLayout();
        }
        static void CaptureColumns(Control control, List<KeyValuePair<ColumnHeader, int>> columns) {
            var list = control as ListView;
            if (list != null) foreach (ColumnHeader column in list.Columns) if (column.Width >= 0) columns.Add(new KeyValuePair<ColumnHeader, int>(column, column.Width));
            foreach (Control child in control.Controls) CaptureColumns(child, columns);
        }
        static void ScaleFonts(Control control, float factor) {
            // Explicit descendant fonts first (inherited fonts share their parent's instance), then this one.
            ScaleExplicitDescendantFonts(control, factor);
            control.Font = new Font(control.Font.FontFamily, control.Font.Size * factor, control.Font.Style, control.Font.Unit);
        }
        static void ScaleExplicitDescendantFonts(Control control, float factor) {
            foreach (Control child in control.Controls) { if (ReferenceEquals(child.Font, control.Font)) ScaleExplicitDescendantFonts(child, factor); else ScaleFonts(child, factor); }
        }
        static Size ScaleBounds(Size value, SizeF factor) {
            return new Size((int)Math.Round(value.Width * factor.Width), (int)Math.Round(value.Height * factor.Height));
        }
        protected override void OnLoad(EventArgs e) { PrepareDpiLayout(); base.OnLoad(e); }
    }
}
