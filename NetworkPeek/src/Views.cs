using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetworkPeek
{
    internal static class Theme
    {
        internal static readonly Color Background = Color.FromArgb(20, 26, 36);
        internal static readonly Color Card = Color.FromArgb(30, 39, 54);
        internal static readonly Color Border = Color.FromArgb(53, 67, 87);
        internal static readonly Color Muted = Color.FromArgb(151, 168, 189);
        internal static readonly Color Accent = Color.FromArgb(89, 213, 181);
        internal static Label Label(string text, float size, Color color, int x, int y, int width, int height, bool bold)
        {
            return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, Bounds = new Rectangle(x, y, width, height), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        }
        internal static Button Button(string text, int x, int y, int width, Action action)
        {
            var button = new Button { Text = text, Bounds = new Rectangle(x, y, width, 34), BackColor = Card, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = true };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 57, 75);
            button.Click += delegate { action(); };
            return button;
        }
        internal static void FitText(Label label, float maximumSize)
        {
            if (label.ClientSize.Width < 8 || label.ClientSize.Height < 8) return;
            float size = maximumSize;
            using (var graphics = label.CreateGraphics())
            {
                while (size > 7)
                {
                    using (var font = new Font(label.Font.FontFamily, size, label.Font.Style))
                    {
                        var measured = TextRenderer.MeasureText(graphics, label.Text, font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.SingleLine);
                        if (measured.Width <= label.ClientSize.Width - 4 && measured.Height <= label.ClientSize.Height - 2) break;
                    }
                    size -= 0.5f;
                }
            }
            if (Math.Abs(label.Font.Size - size) < 0.1f) return;
            var old = label.Font;
            label.Font = new Font(old.FontFamily, size, old.Style);
            old.Dispose();
        }
    }

    internal class PeekForm : Form
    {
        protected PeekForm()
        {
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9);
            BackColor = Theme.Background;
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Text = "NetworkPeek";
        }
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x80; return value; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    internal sealed class CompactBar : PeekForm
    {
        internal event Action OpenRequested;
        internal event Action PositionChanged;
        private readonly Label address;
        private readonly Label gateway;
        private readonly Label dot;
        private readonly Label ipCaption;
        private readonly Label gatewayCaption;
        private readonly Label divider;
        private Point dragStart;
        private Point originalPosition;
        private bool mouseDown;
        private bool dragging;

        internal CompactBar()
        {
            ClientSize = new Size(366, 42);
            Cursor = Cursors.Hand;
            AccessibleName = "NetworkPeek IP 표시줄";
            dot = Theme.Label("●", 8, Theme.Muted, 12, 0, 14, 42, false);
            address = Theme.Label("확인 중…", 11, Color.White, 49, 0, 143, 42, true);
            gateway = Theme.Label("—", 10, Theme.Muted, 231, 0, 126, 42, false);
            ipCaption = Theme.Label("IP", 8, Theme.Muted, 28, 0, 22, 42, true);
            gatewayCaption = Theme.Label("GW", 8, Theme.Muted, 204, 0, 28, 42, true);
            divider = Theme.Label("│", 12, Theme.Border, 190, 0, 14, 42, false);
            ipCaption.AutoSize = gatewayCaption.AutoSize = true;
            Controls.Add(dot);
            Controls.Add(ipCaption);
            Controls.Add(address);
            Controls.Add(divider);
            Controls.Add(gatewayCaption);
            Controls.Add(gateway);
            foreach (Control control in Controls) HookMouse(control);
            HookMouse(this);
            Shown += delegate { FitAddresses(); ClampToScreen(); };
            DpiChanged += delegate { BeginInvoke((Action)FitAddresses); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        internal Point DefaultPosition()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            return new Point(area.Right - Width - 12, area.Bottom - Height - 10);
        }
        internal void ClampToScreen()
        {
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }
        private void HookMouse(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                mouseDown = true;
                dragging = false;
                dragStart = MousePosition;
                originalPosition = Location;
                Capture = true;
            };
            control.MouseMove += delegate
            {
                if (!mouseDown) return;
                var delta = new Point(MousePosition.X - dragStart.X, MousePosition.Y - dragStart.Y);
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 4) dragging = true;
                if (dragging) Location = new Point(originalPosition.X + delta.X, originalPosition.Y + delta.Y);
            };
            control.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (!mouseDown || e.Button != MouseButtons.Left) return;
                mouseDown = false;
                Capture = false;
                if (dragging) { ClampToScreen(); if (PositionChanged != null) PositionChanged(); }
                else if (OpenRequested != null) OpenRequested();
            };
        }
        internal void UpdateSnapshot(NetworkSnapshot snapshot)
        {
            address.Text = snapshot.Primary == null ? "연결 없음" : snapshot.Primary.IPv4;
            gateway.Text = snapshot.Primary == null ? "—" : snapshot.Primary.Gateway;
            dot.ForeColor = snapshot.Primary == null ? Theme.Muted : Theme.Accent;
            AccessibleDescription = "IP " + address.Text + ", 게이트웨이 " + gateway.Text;
            FitAddresses();
        }
        private void FitAddresses()
        {
            int gap = Math.Max(4, ClientSize.Height / 7);
            ipCaption.Top = (ClientSize.Height - ipCaption.Height) / 2;
            gatewayCaption.Top = (ClientSize.Height - gatewayCaption.Height) / 2;
            address.Left = ipCaption.Right + gap;
            address.Width = Math.Max(1, divider.Left - address.Left - gap);
            gateway.Left = gatewayCaption.Right + gap;
            gateway.Width = Math.Max(1, ClientSize.Width - gateway.Left - gap * 2);
            Theme.FitText(address, 11);
            Theme.FitText(gateway, 10);
        }
    }

    internal sealed class AddressRow : Button
    {
        private readonly string caption;
        private bool hover;

        internal AddressRow(string caption, int y, bool primary)
        {
            this.caption = caption;
            Bounds = new Rectangle(14, y, 292, 36);
            Font = new Font("Consolas", 13, primary ? FontStyle.Bold : FontStyle.Regular);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Background;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = Height / 36f;
            bool highlighted = Enabled && (hover || Focused);
            e.Graphics.Clear(highlighted ? Theme.Card : Theme.Background);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            using (var keyFont = new Font("Segoe UI", 8.5f))
                TextRenderer.DrawText(e.Graphics, caption, keyFont, new Rectangle((int)(8 * scale), 0, (int)(42 * scale), Height), Theme.Muted, flags);
            var valueBounds = new Rectangle((int)(58 * scale), 0, Width - (int)(86 * scale), Height);
            var measured = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(Int32.MaxValue, Int32.MaxValue), flags);
            float fit = Math.Min(1f, Math.Min((valueBounds.Width - 2f) / Math.Max(1, measured.Width), (Height - 4f) / Math.Max(1, measured.Height)));
            using (var valueFont = new Font(Font.FontFamily, Math.Max(7, Font.Size * fit), Font.Style))
                TextRenderer.DrawText(e.Graphics, Text, valueFont, valueBounds, Enabled ? Color.White : Theme.Muted, flags);
            if (Enabled)
            {
                using (var pen = new Pen(highlighted ? Theme.Accent : Theme.Muted, scale))
                {
                    float x = Width - 20 * scale, y = Height / 2f - 4 * scale;
                    e.Graphics.DrawRectangle(pen, x, y, 7 * scale, 9 * scale);
                    e.Graphics.DrawLines(pen, new[] { new PointF(x - 3 * scale, y + 6 * scale), new PointF(x - 3 * scale, y - 3 * scale), new PointF(x + 4 * scale, y - 3 * scale) });
                }
            }
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), Theme.Muted, Theme.Card);
        }

        internal void SetAddress(string value, bool available)
        {
            Text = value;
            Enabled = available;
            Cursor = available ? Cursors.Hand : Cursors.Default;
            AccessibleName = caption + " " + value + (available ? " 복사" : "");
        }
    }

    internal sealed class DetailsWindow : PeekForm
    {
        internal event Action<string> CopyRequested;
        private readonly Label connection;
        private readonly Label feedback;
        private readonly AddressRow address;
        private readonly AddressRow gateway;
        private readonly Timer feedbackTimer;
        private NetworkSnapshot snapshot = new NetworkSnapshot();

        internal DetailsWindow()
        {
            ClientSize = new Size(320, 148);
            AccessibleName = "NetworkPeek IP와 게이트웨이";
            connection = Theme.Label("확인 중", 9, Theme.Muted, 22, 10, 244, 24, false);
            Controls.Add(connection);
            var close = Theme.Button("×", 278, 8, 28, Hide);
            close.Height = 28;
            close.BackColor = Theme.Background;
            close.ForeColor = Theme.Muted;
            close.FlatAppearance.BorderSize = 0;
            close.TabStop = false;
            close.AccessibleName = "닫기";
            Controls.Add(close);

            address = new AddressRow("IP", 43, true) { TabIndex = 0 };
            gateway = new AddressRow("GW", 83, false) { TabIndex = 1 };
            address.Click += delegate { if (snapshot.Primary != null) Copy(snapshot.Primary.IPv4); };
            gateway.Click += delegate { if (snapshot.Primary != null) Copy(snapshot.Primary.Gateway); };
            Controls.Add(address);
            Controls.Add(gateway);
            feedback = Theme.Label("클릭해 복사", 8, Theme.Muted, 22, 122, 278, 18, false);
            Controls.Add(feedback);
            feedbackTimer = new Timer { Interval = 1600 };
            feedbackTimer.Tick += delegate { ResetFeedback(); };
            VisibleChanged += delegate { ResetFeedback(); };
            Deactivate += delegate { Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            Shown += delegate { ActiveControl = null; };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.C)
                {
                    if (e.Shift) Copy(snapshot.ToReport());
                    else if (snapshot.Primary != null) Copy(ActiveControl == gateway ? snapshot.Primary.Gateway : snapshot.Primary.IPv4);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void Copy(string value) { if (CopyRequested != null) CopyRequested(value); }
        private void ResetFeedback()
        {
            feedbackTimer.Stop();
            feedback.Text = "클릭해 복사";
            feedback.ForeColor = Theme.Muted;
        }
        internal void ShowFeedback(string message)
        {
            feedbackTimer.Stop();
            feedback.Text = message;
            feedback.ForeColor = Theme.Accent;
            feedbackTimer.Start();
        }
        internal void PlaceNear(CompactBar bar)
        {
            var screen = bar == null ? Screen.FromPoint(Cursor.Position) : Screen.FromControl(bar);
            var area = screen.WorkingArea;
            int x = bar == null ? area.Right - Width - 12 : bar.Right - Width;
            int y = bar == null ? area.Bottom - Height - 12 : bar.Top - Height - 8;
            if (bar != null && y < area.Top) y = bar.Bottom + 8;
            Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - Height)));
        }
        internal void UpdateSnapshot(NetworkSnapshot value)
        {
            snapshot = value;
            var primary = snapshot.Primary;
            connection.Text = primary == null ? "연결 없음" : primary.Name;
            address.SetAddress(primary == null ? "—" : primary.IPv4, primary != null);
            gateway.SetAddress(primary == null ? "—" : primary.Gateway, primary != null);
            AccessibleDescription = !String.IsNullOrEmpty(snapshot.Error) ? snapshot.Error : connection.Text;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) feedbackTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
