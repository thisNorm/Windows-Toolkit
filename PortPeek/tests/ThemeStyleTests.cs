using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PortPeek;

internal static class ThemeStyleTests
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        try
        {
            foreach (var palette in new[] { ThemeColors.Light, ThemeColors.Dark }) {
                Check(Contrast(palette.Text, palette.Background) >= 4.5, "Body text contrast is too low.");
                Check(Contrast(palette.Muted, palette.Background) >= 4.5, "Muted text contrast is too low.");
                Check(Contrast(palette.Text, palette.Selection) >= 4.5, "Selected row text contrast is too low.");
                Check(Contrast(palette.Favorite, palette.Background) >= 3, "Favorite stars are hard to distinguish.");
            }
            using (var root = new Panel { BackColor = ThemeColors.Light.Background, ForeColor = ThemeColors.Light.Text })
            using (var grid = new PortGrid()) {
                var search = new TextBox { BackColor = ThemeColors.Light.Surface, ForeColor = ThemeColors.Light.Text };
                var label = new Label { ForeColor = ThemeColors.Light.Muted };
                var danger = new Button { FlatStyle = FlatStyle.Flat, ForeColor = ThemeColors.Light.Danger, BackColor = ThemeColors.Light.Background };
                root.Controls.Add(search); root.Controls.Add(label); root.Controls.Add(danger); root.Controls.Add(grid);
                grid.AllowUserToAddRows = false;
                grid.Columns.Add("Port", "Port"); grid.Columns.Add("Protocol", "Protocol"); grid.AddFavoriteColumns();
                grid.Rows.Add(8080, "UDP", "★", "☆");
                grid.Rows[0].Tag = new PortEntry { Port = 8080, Protocol = "UDP", ProcessName = "node" };
                ThemePainter.Apply(root, ThemeColors.Light, ThemeColors.Dark);
                Check(root.BackColor == ThemeColors.Dark.Background && search.BackColor == ThemeColors.Dark.Surface, "Nested surfaces did not switch to dark.");
                Check(label.ForeColor == ThemeColors.Dark.Muted && danger.ForeColor == ThemeColors.Dark.Danger, "Semantic control colors did not switch.");
                Check(grid.DefaultCellStyle.SelectionBackColor == ThemeColors.Dark.Selection, "Selection remains light.");
                Check(grid.Rows[0].Cells["FavoritePort"].Style.ForeColor == ThemeColors.Dark.Favorite, "Favorite star did not switch.");
                Check(grid.Rows[0].Cells["Protocol"].Style.ForeColor == ThemeColors.Dark.Udp, "UDP text did not switch.");
                ThemePainter.Apply(root, ThemeColors.Dark, ThemeColors.Light);
                Check(search.BackColor == ThemeColors.Light.Surface && label.ForeColor == ThemeColors.Light.Muted, "Switching back to light lost semantic colors.");
                Check(grid.Rows[0].Cells["FavoritePort"].Style.ForeColor == ThemeColors.Light.Favorite, "Favorite color did not restore.");
            }
            using (var menu = new ContextMenuStrip()) {
                var modes = new ToolStripMenuItem("Theme"); modes.DropDownItems.Add("Dark"); menu.Items.Add(modes);
                ThemePainter.ApplyMenu(menu, ThemeColors.Dark);
                Check(menu.BackColor == ThemeColors.Dark.Background && modes.DropDown.BackColor == ThemeColors.Dark.Background, "Submenus do not follow dark mode.");
            }
            QuickWindowDarkRows();
            Console.WriteLine("PASS: light/dark contrast, nested surfaces, semantic colors, favorite stars, grid selection, submenu styling and real QuickWindow inherited styles through rebuild and round trip.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static void QuickWindowDarkRows()
    {
        string folder = Path.Combine(Path.GetTempPath(), "PortPeek-ThemeStyleTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            string file = Path.Combine(folder, "favorites.txt");
            File.WriteAllLines(file, new[] { "port:8080", "program:worker" });
            using (var window = new QuickWindow(FavoriteStore.Load(file))) {
                var errors = new List<string>();
                var grid = Descendants(window).OfType<PortGrid>().Single();
                window.ApplyTheme(ThemeColors.Dark);
                CheckQuickColors(window, ThemeColors.Dark, "dark before rows", errors, 0);
                var snapshot = new PortSnapshot { Entries = new[] {
                    new PortEntry { Port = 8080, ProcessId = 101, Protocol = "TCP", ProcessName = "node" },
                    new PortEntry { Port = 5353, ProcessId = 102, Protocol = "UDP", ProcessName = "worker" },
                    new PortEntry { Port = 9000, ProcessId = 103, Protocol = "TCP", ProcessName = "service" }
                } };
                window.UpdateSnapshot(snapshot);
                CheckQuickColors(window, ThemeColors.Dark, "initial dark rows", errors);
                var original = grid.Rows[0];
                window.UpdateSnapshot(new PortSnapshot { Entries = new[] {
                    snapshot.Entries[0], snapshot.Entries[1],
                    new PortEntry { Port = 9001, ProcessId = 104, Protocol = "UDP", ProcessName = "rebuilt" }
                } });
                Check(!Object.ReferenceEquals(original, grid.Rows[0]), "Snapshot fixture did not rebuild real rows.");
                CheckQuickColors(window, ThemeColors.Dark, "rebuilt dark rows", errors);
                window.ApplyTheme(ThemeColors.Light);
                CheckQuickColors(window, ThemeColors.Light, "light round trip", errors);
                window.ApplyTheme(ThemeColors.Dark);
                CheckQuickColors(window, ThemeColors.Dark, "dark round trip", errors);
                Check(!window.Visible, "Theme regression test must not show QuickWindow.");
                Check(errors.Count == 0, String.Join(Environment.NewLine, errors));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void CheckQuickColors(QuickWindow window, ThemeColors colors, string stage, List<string> errors, int expectedRows = 3)
    {
        var controls = Descendants(window).ToArray();
        CheckColor(controls.OfType<Label>().Single(c => c.Text == "PortPeek").ForeColor, colors.Text, stage + " title.ForeColor", errors);
        var search = controls.OfType<TextBox>().Single();
        CheckColor(search.ForeColor, colors.Text, stage + " search.ForeColor", errors);
        CheckColor(search.BackColor, colors.Surface, stage + " search.BackColor", errors);
        var grid = controls.OfType<PortGrid>().Single();
        Check(grid.Rows.Count == expectedRows, stage + ": expected " + expectedRows + " fixture rows.");
        foreach (DataGridViewRow row in grid.Rows) {
            var entry = (PortEntry)row.Tag;
            foreach (DataGridViewCell cell in row.Cells) {
                Color text = colors.Text, selectedText = colors.Text;
                switch (cell.OwningColumn.Name) {
                    case "Port": text = selectedText = colors.Accent; break;
                    case "Protocol":
                        text = entry.Protocol == "UDP" ? colors.Udp : colors.Muted;
                        selectedText = entry.Protocol == "UDP" ? colors.Udp : colors.Text;
                        break;
                    case "FavoritePort": case "FavoriteProgram":
                        text = selectedText = (string)cell.Value == "★" ? colors.Favorite : colors.EmptyStar;
                        break;
                }
                var style = cell.InheritedStyle;
                string name = stage + " row " + row.Index + " " + cell.OwningColumn.Name + ".InheritedStyle.";
                CheckColor(style.BackColor, row.Index % 2 == 0 ? colors.Background : colors.Alternate, name + "BackColor", errors);
                CheckColor(style.ForeColor, text, name + "ForeColor", errors);
                CheckColor(style.SelectionBackColor, colors.Selection, name + "SelectionBackColor", errors);
                CheckColor(style.SelectionForeColor, selectedText, name + "SelectionForeColor", errors);
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    private static void CheckColor(Color actual, Color expected, string name, List<string> errors)
    {
        if (actual.ToArgb() != expected.ToArgb())
            errors.Add(name + ": actual #" + actual.ToArgb().ToString("X8") + ", expected #" + expected.ToArgb().ToString("X8"));
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static double Contrast(Color left, Color right)
    {
        double a = Luminance(left), b = Luminance(right);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static double Luminance(Color value)
    {
        return 0.2126 * Linear(value.R) + 0.7152 * Linear(value.G) + 0.0722 * Linear(value.B);
    }
    private static double Linear(byte value)
    {
        double s = value / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
