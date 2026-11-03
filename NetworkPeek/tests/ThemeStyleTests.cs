using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using NetworkPeek;

internal static class ThemeStyleTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        var original = Theme.Current;
        try
        {
            var adapter = new AdapterInfo { Name = "Wi-Fi", IPv4 = "192.0.2.123", Gateway = "192.0.2.1" };
            var snapshot = new NetworkSnapshot { Primary = adapter, Adapters = new[] { adapter }, CapturedAt = DateTime.Now };
            using (var details = new DetailsWindow())
            using (var bar = new CompactBar())
            using (var preview = new Bitmap(764, 220))
            {
                details.UpdateSnapshot(snapshot); bar.UpdateSnapshot(snapshot);
                string copied = null;
                details.CopyRequested += value => copied = value;
                using (var graphics = Graphics.FromImage(preview)) {
                    graphics.Clear(Color.FromArgb(226, 232, 240));
                    int column = 0;
                    foreach (var palette in new[] { ThemeColors.Light, ThemeColors.Dark, ThemeColors.Light }) {
                        Theme.Current = palette;
                        details.ApplyTheme(); bar.ApplyTheme();
                        details.SetThemeMode(palette.IsDark ? ThemeMode.Dark : ThemeMode.Light);
                        Check(details.BackColor == palette.Background && bar.BackColor == palette.Background, "Popup and compact bar must follow the same theme.");
                        Check(Contrast(palette.Text, palette.Background) >= 4.5 && Contrast(palette.Muted, palette.Background) >= 4.5, "Theme text needs readable contrast.");
                        var rows = details.Controls.OfType<AddressRow>().ToArray();
                        Check(rows.Length == 2 && rows.All(row => row.Enabled), "Both addresses remain actionable.");
                        using (var image = new Bitmap(rows[0].Width, rows[0].Height)) {
                            rows[0].DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                            bool textVisible = false;
                            for (int x = 58; x < image.Width - 28 && !textVisible; x++)
                                for (int y = 2; y < image.Height - 2; y++)
                                    if (image.GetPixel(x, y).ToArgb() == palette.Text.ToArgb()) { textVisible = true; break; }
                            Check(textVisible, "IP text must be painted in the active theme foreground.");
                        }
                        RaiseClick(rows[0]); Check(copied == adapter.IPv4, "IP click must copy only IPv4.");
                        RaiseClick(rows[1]); Check(copied == adapter.Gateway, "Gateway click must copy only gateway.");
                        foreach (var label in bar.Controls.OfType<Label>().Where(label => label.Text == "IP" || label.Text == "GW")) {
                            using (var canvas = label.CreateGraphics()) {
                                var measured = TextRenderer.MeasureText(canvas, label.Text, label.Font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                                Check(measured.Width <= label.Width, "IP/GW caption " + label.Text + " requires " + measured.Width + "px but has " + label.Width + "px.");
                            }
                        }
                        if (column < 2) {
                            using (var popup = RenderHidden(details)) {
                                graphics.DrawImageUnscaled(popup, 14 + column * 380, 12);
                            }
                            using (var compact = RenderHidden(bar)) {
                                graphics.DrawImageUnscaled(compact, 14 + column * 380, 170);
                            }
                        }
                        column++;
                    }
                }
                details.UpdateSnapshot(new NetworkSnapshot());
                Check(details.Controls.OfType<AddressRow>().All(row => !row.Enabled), "Disconnected addresses must not copy stale data.");
                if (args.Length == 1) preview.Save(args[0], ImageFormat.Png);
            }
            Console.WriteLine("PASS: rendered light/dark UI, popup/bar synchronization, text contrast, IP/GW captions, copy actions and disconnected state.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error.Message); return 1; }
        finally { Theme.Current = original; }
    }
    // A hidden form omits its children in DrawToBitmap, so render each actual control at its layout bounds.
    private static Bitmap RenderHidden(Control parent)
    {
        var result = new Bitmap(parent.Width, parent.Height);
        parent.DrawToBitmap(result, new Rectangle(Point.Empty, result.Size));
        using (var graphics = Graphics.FromImage(result))
            foreach (Control child in parent.Controls)
                using (var image = new Bitmap(child.Width, child.Height)) {
                    child.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    graphics.DrawImageUnscaled(image, child.Location);
                }
        return result;
    }
    // PerformClick requires a visible parent; invoke the same click event without showing test windows.
    private static void RaiseClick(Button button)
    {
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(button, new object[] { EventArgs.Empty });
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static double Contrast(Color left, Color right)
    {
        double a = Luminance(left), b = Luminance(right);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static double Luminance(Color value) { return 0.2126 * Linear(value.R) + 0.7152 * Linear(value.G) + 0.0722 * Linear(value.B); }
    private static double Linear(byte value) { double s = value / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
}
