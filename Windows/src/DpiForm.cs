using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    // Source layouts use 96-DPI logical dimensions. Prepare once after the derived
    // constructor has assembled the complete layout, before a modal/window is shown.
    internal abstract class DpiForm : Form {
        bool prepared;
        internal void PrepareDpiLayout() {
            if (prepared) return;
            prepared = true;
            SuspendLayout();
            Size client = ClientSize, minimum = MinimumSize, maximum = MaximumSize;
            AutoScaleMode = AutoScaleMode.Dpi;
            SizeF current = CurrentAutoScaleDimensions;
            var factor = new SizeF(current.Width / 96, current.Height / 96);
            var columns = new List<KeyValuePair<ColumnHeader, int>>(); CaptureColumns(this, columns);
            AutoScaleDimensions = new SizeF(96, 96);
            ResumeLayout(false); PerformAutoScale();
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
        static Size ScaleBounds(Size value, SizeF factor) {
            return new Size((int)Math.Round(value.Width * factor.Width), (int)Math.Round(value.Height * factor.Height));
        }
        protected override void OnLoad(EventArgs e) { PrepareDpiLayout(); base.OnLoad(e); }
    }
}
