using System.Drawing;
using System.Windows.Forms;

namespace NetworkPeek
{
    internal sealed class ThemeColors
    {
        internal static readonly ThemeColors Light = new ThemeColors(false);
        internal static readonly ThemeColors Dark = new ThemeColors(true);
        internal readonly bool IsDark;
        internal readonly Color Background, Text, Muted, Card, Hover, Accent;
        internal Color Surface { get { return Card; } }

        private ThemeColors(bool dark)
        {
            IsDark = dark;
            Background = dark ? Color.FromArgb(21, 28, 38) : Color.White;
            Text = dark ? Color.FromArgb(230, 236, 245) : Color.FromArgb(31, 41, 55);
            Muted = dark ? Color.FromArgb(157, 170, 188) : Color.FromArgb(107, 114, 128);
            Card = dark ? Color.FromArgb(34, 42, 54) : Color.FromArgb(244, 246, 249);
            Hover = dark ? Color.FromArgb(45, 55, 70) : Color.FromArgb(234, 238, 245);
            Accent = dark ? Color.FromArgb(114, 220, 199) : Color.FromArgb(15, 118, 110);
        }
    }

    internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemeColors colors;
        internal ThemeMenuRenderer(ThemeColors colors) : base(new ThemeMenuColors(colors)) { this.colors = colors; RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? colors.Text : colors.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? colors.Text : colors.Muted;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var bounds = e.ImageRectangle;
            using (var pen = new Pen(colors.Accent, 2))
                e.Graphics.DrawLines(pen, new[] { new Point(bounds.Left + 2, bounds.Top + bounds.Height / 2), new Point(bounds.Left + bounds.Width / 2 - 1, bounds.Bottom - 3), new Point(bounds.Right - 1, bounds.Top + 3) });
        }
    }

    internal sealed class ThemeMenuColors : ProfessionalColorTable
    {
        private readonly ThemeColors colors;
        internal ThemeMenuColors(ThemeColors colors) { this.colors = colors; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return colors.Background; } }
        public override Color MenuBorder { get { return colors.Surface; } }
        public override Color MenuItemBorder { get { return colors.Hover; } }
        public override Color MenuItemSelected { get { return colors.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return colors.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return colors.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return colors.Hover; } }
        public override Color MenuItemPressedGradientMiddle { get { return colors.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return colors.Hover; } }
        public override Color ImageMarginGradientBegin { get { return colors.Background; } }
        public override Color ImageMarginGradientMiddle { get { return colors.Background; } }
        public override Color ImageMarginGradientEnd { get { return colors.Background; } }
        public override Color CheckBackground { get { return colors.Background; } }
        public override Color CheckSelectedBackground { get { return colors.Hover; } }
        public override Color CheckPressedBackground { get { return colors.Hover; } }
        public override Color SeparatorDark { get { return colors.Surface; } }
        public override Color SeparatorLight { get { return colors.Surface; } }
    }
}
