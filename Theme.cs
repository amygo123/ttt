
using System.Drawing;

namespace StyleWatcherWin
{
    /// <summary>
    /// Centralized visual tokens to unify the desktop UI.
    /// Purely visual — no behavior change.
    /// </summary>
    public static class Theme
    {
        // Typography
        public static readonly Font FontBase      = new("Microsoft YaHei UI", 10f, FontStyle.Regular);
        public static readonly Font FontSm        = new("Microsoft YaHei UI", 9f,  FontStyle.Regular);
        public static readonly Font FontXs        = new("Microsoft YaHei UI", 8.5f,FontStyle.Regular);
        public static readonly Font FontTitle     = new("Microsoft YaHei UI", 12f, FontStyle.Bold);
        public static readonly Font FontKpi       = new("Microsoft YaHei UI", 16f, FontStyle.Bold);

        // Palette (AA contrast on white backgrounds)
        public static readonly Color Text         = Color.FromArgb(45, 45, 45);
        public static readonly Color SubtleText   = Color.FromArgb(110, 110, 110);
        public static readonly Color Accent       = Color.FromArgb(32, 96, 240);
        public static readonly Color AccentMuted  = Color.FromArgb(218, 230, 255);

        public static readonly Color Surface      = Color.White;
        public static readonly Color SurfaceAlt   = Color.FromArgb(250, 250, 252);
        public static readonly Color Header       = Color.FromArgb(245, 247, 250);

        public static readonly Color Border       = Color.FromArgb(228, 232, 244);
        public static readonly Color GridLine     = Color.FromArgb(230, 235, 245);
        public static readonly Color RowAlt       = Color.FromArgb(249, 251, 255);

        // Status colors
        public static readonly Color Good         = Color.FromArgb(26, 127, 55);
        public static readonly Color Caution      = Color.FromArgb(216, 160, 18);
        public static readonly Color Danger       = Color.FromArgb(215, 58, 73);

        // Chips
        public static readonly Color ChipBack     = Color.FromArgb(235, 238, 244);
        public static readonly Color ChipBorder   = Color.FromArgb(210, 214, 222);

        // Helpers
        public static void StylePrimaryButton(System.Windows.Forms.Button b)
        {
            b.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.BackColor = Surface;
            b.ForeColor = Text;
            b.Padding = new System.Windows.Forms.Padding(10, 5, 10, 5);
            b.AutoSize = true;
        }

        public static void StyleDataGrid(System.Windows.Forms.DataGridView g)
        {
            g.EnableHeadersVisualStyles = false;
            g.BorderStyle = System.Windows.Forms.BorderStyle.None;
            g.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = GridLine;
            g.ColumnHeadersDefaultCellStyle.BackColor = Header;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            g.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
            g.DefaultCellStyle.ForeColor = Text;
        }

        public static void StyleCard(System.Windows.Forms.Panel p)
        {
            p.BackColor = SurfaceAlt;
            p.Padding = new System.Windows.Forms.Padding(10);
            p.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
            p.Paint += (s, e) =>
            {
                var r = p.ClientRectangle; r.Inflate(-1, -1);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var pen = new Pen(Border);
                e.Graphics.DrawRectangle(pen, r);
            };
        }
    }
}
