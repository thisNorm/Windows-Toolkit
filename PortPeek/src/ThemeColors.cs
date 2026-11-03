using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PortPeek
{
    internal sealed class ThemeColors
    {
        internal static readonly ThemeColors Light = new ThemeColors(false);
        internal static readonly ThemeColors Dark = new ThemeColors(true);
        internal readonly bool IsDark;
        internal readonly Color Background, Text, Muted, Surface, Header, Alternate, Accent, Selection;
        internal readonly Color Hover, Pressed, FilterSelected, FilterHover, Danger, Favorite, EmptyStar, Udp;

        private ThemeColors(bool dark)
        {
            IsDark = dark;
            Background = dark ? Color.FromArgb(21, 28, 38) : Color.White;
            Text = dark ? Color.FromArgb(230, 236, 245) : Color.FromArgb(31, 41, 55);
            Muted = dark ? Color.FromArgb(157, 170, 188) : Color.FromArgb(107, 114, 128);
            Surface = dark ? Color.FromArgb(34, 42, 54) : Color.FromArgb(244, 246, 249);
            Header = dark ? Color.FromArgb(28, 36, 48) : Color.FromArgb(247, 249, 252);
            Alternate = dark ? Color.FromArgb(25, 32, 43) : Color.FromArgb(250, 251, 253);
            Accent = dark ? Color.FromArgb(147, 197, 253) : Color.FromArgb(37, 99, 235);
            Selection = dark ? Color.FromArgb(36, 59, 87) : Color.FromArgb(233, 240, 255);
            Hover = dark ? Color.FromArgb(45, 55, 70) : Color.FromArgb(246, 248, 251);
            Pressed = dark ? Color.FromArgb(56, 72, 95) : Color.FromArgb(232, 237, 245);
            FilterSelected = dark ? Color.FromArgb(37, 59, 96) : Color.FromArgb(226, 235, 253);
            FilterHover = dark ? Color.FromArgb(44, 54, 70) : Color.FromArgb(234, 238, 245);
            Danger = dark ? Color.FromArgb(253, 164, 164) : Color.FromArgb(185, 51, 51);
            Favorite = dark ? Color.FromArgb(246, 196, 106) : Color.FromArgb(180, 120, 22);
            EmptyStar = dark ? Color.FromArgb(131, 147, 169) : Color.FromArgb(112, 128, 144);
            Udp = dark ? Color.FromArgb(114, 220, 199) : Color.FromArgb(15, 118, 110);
        }

        internal Color MapFrom(ThemeColors previous, Color value)
        {
            if (value.IsEmpty || value.A == 0) return value;
            Color[] before = previous.Values(), after = Values();
            for (int i = 0; i < before.Length; i++)
                if (before[i].ToArgb() == value.ToArgb()) return after[i];
            return value;
        }

        private Color[] Values()
        {
            return new[] { Background, Text, Muted, Surface, Header, Alternate, Accent, Selection, Hover, Pressed, FilterSelected, FilterHover, Danger, Favorite, EmptyStar, Udp };
        }
    }

    internal static class ThemePainter
    {
        internal static void Apply(Control control, ThemeColors previous, ThemeColors colors)
        {
            var grid = control as PortGrid;
            if (grid != null) { grid.ApplyTheme(colors); return; }
            control.BackColor = colors.MapFrom(previous, control.BackColor);
            Color foreground = control.ForeColor;
            control.ForeColor = foreground == SystemColors.ControlText || foreground == SystemColors.WindowText
                ? colors.Text : colors.MapFrom(previous, foreground);
            var search = control as ThemedSearchBox;
            if (search != null) { search.HintColor = colors.Muted; search.Invalidate(); }
            var button = control as Button;
            if (button != null) {
                button.FlatAppearance.MouseOverBackColor = colors.Hover;
                button.FlatAppearance.MouseDownBackColor = colors.Pressed;
            }
            var themedButton = control as ThemedButton;
            if (themedButton != null) themedButton.DisabledTextColor = colors.Muted;
            var radio = control as RadioButton;
            if (radio != null) {
                radio.BackColor = colors.Surface;
                radio.ForeColor = radio.Checked ? colors.Accent : colors.Muted;
                radio.FlatAppearance.CheckedBackColor = colors.FilterSelected;
                radio.FlatAppearance.MouseOverBackColor = colors.FilterHover;
            }
            var link = control as LinkLabel;
            if (link != null) link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = colors.Accent;
            foreach (Control child in control.Controls) Apply(child, previous, colors);
        }

        internal static void ApplyMenu(ToolStrip menu, ThemeColors colors)
        {
            menu.BackColor = colors.Background;
            menu.ForeColor = colors.Text;
            menu.Renderer = new ThemeMenuRenderer(colors);
            foreach (ToolStripItem item in menu.Items) {
                item.BackColor = colors.Background;
                item.ForeColor = colors.Text;
                var dropdown = item as ToolStripDropDownItem;
                if (dropdown != null && dropdown.HasDropDownItems) ApplyMenu(dropdown.DropDown, colors);
            }
        }

        internal static void ApplyTitleBar(Form form, ThemeColors colors)
        {
            if (!form.IsHandleCreated) return;
            int dark = colors.IsDark ? 1 : 0;
            int background = colors.Background.R | (colors.Background.G << 8) | (colors.Background.B << 16);
            int foreground = colors.Text.R | (colors.Text.G << 8) | (colors.Text.B << 16);
            try {
                DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                DwmSetWindowAttribute(form.Handle, 35, ref background, sizeof(int));
                DwmSetWindowAttribute(form.Handle, 36, ref foreground, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    internal sealed class ThemedButton : Button
    {
        internal Color DisabledTextColor = ThemeColors.Light.Muted;
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, DisabledTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class ThemedSearchBox : TextBox
    {
        private readonly string hint;
        internal Color HintColor = ThemeColors.Light.Muted;
        internal ThemedSearchBox(string hint) { this.hint = hint; }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            // Native cue banners keep the Windows light-theme gray on custom dark surfaces.
            if ((message.Msg == 0x000F || message.Msg == 0x0318) && TextLength == 0 && ClientSize.Width > 2)
                using (var canvas = message.WParam == IntPtr.Zero ? CreateGraphics() : Graphics.FromHdc(message.WParam))
                    TextRenderer.DrawText(canvas, hint, Font, new Rectangle(1, 0, ClientSize.Width - 2, ClientSize.Height),
                        HintColor, BackColor, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
