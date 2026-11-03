using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PortPeek
{
    internal sealed class QuickWindow : Form
    {
        internal event Action OpenFullRequested;
        internal event Action<PortEntry> OpenBrowserRequested;
        internal event Action RefreshRequested;
        internal event Action<PortEntry> TerminateRequested;
        private readonly TextBox search;
        private readonly PortGrid grid;
        private readonly Label footer;
        private readonly Label empty;
        private readonly Timer feedbackTimer;
        private readonly ContextMenuStrip rowMenu;
        private readonly FavoriteStore favorites;
        private ThemeColors colors = ThemeColors.Light;
        private PortSnapshot snapshot = new PortSnapshot();
        private PortEntry menuEntry;
        private string signature;
        private bool updating;

        internal QuickWindow(FavoriteStore favorites)
        {
            this.favorites = favorites;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 322);
            Font = new Font("Segoe UI", 9);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Text = "PortPeek 빠른 확인";
            AccessibleName = Text;
            KeyPreview = true;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            Controls.Add(layout);
            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            header.Controls.Add(new Label { Text = "PortPeek", Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new Point(2, 2) });
            var full = new LinkLabel { Text = "전체 목록", Dock = DockStyle.Right, AutoSize = true, LinkColor = Color.FromArgb(37, 99, 235), TextAlign = ContentAlignment.MiddleRight };
            full.LinkClicked += delegate { Hide(); if (OpenFullRequested != null) OpenFullRequested(); };
            header.Controls.Add(full);
            layout.Controls.Add(header, 0, 0);
            var box = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(10, 5, 10, 4), BackColor = Color.FromArgb(244, 246, 249), Margin = new Padding(0, 0, 0, 8) };
            search = new ThemedSearchBox("포트, 프로그램, PID 검색") { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = box.BackColor, AccessibleName = "빠른 포트 검색" };
            box.Controls.Add(search); layout.Controls.Add(box, 0, 1);
            grid = new PortGrid {
                Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                AutoGenerateColumns = false, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                CellBorderStyle = DataGridViewCellBorderStyle.None, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                AccessibleName = "빠른 포트 목록"
            };
            grid.RowTemplate.Height = 32;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            grid.DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 9), Padding = new Padding(6, 0, 4, 0), SelectionBackColor = Color.FromArgb(233, 240, 255), SelectionForeColor = Color.FromArgb(31, 41, 55) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 8), BackColor = Color.FromArgb(247, 249, 252), ForeColor = Color.FromArgb(107, 114, 128), Padding = new Padding(6, 0, 4, 0) };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "포트", Width = 84, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "프로그램", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Protocol", HeaderText = "유형", Width = 65, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns[0].DefaultCellStyle.Font = new Font("Consolas", 10, FontStyle.Bold);
            grid.Columns[0].DefaultCellStyle.ForeColor = Color.FromArgb(37, 99, 235);
            grid.AddFavoriteColumns();
            var list = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            list.Controls.Add(grid);
            empty = new Label { Dock = DockStyle.Fill, Text = "포트 확인 중…", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(107, 114, 128), BackColor = Color.White };
            list.Controls.Add(empty); layout.Controls.Add(list, 0, 2);
            footer = new Label { Dock = DockStyle.Fill, Text = "클릭하면 포트 번호 복사", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(107, 114, 128), Font = new Font("Segoe UI", 8) };
            layout.Controls.Add(footer, 0, 3);
            feedbackTimer = new Timer { Interval = 1600 };
            rowMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("브라우저 열기", null, delegate {
                var entry = menuEntry; if (entry != null && entry.BrowserUrl != null && OpenBrowserRequested != null) OpenBrowserRequested(entry);
            });
            var copyItem = new ToolStripMenuItem("포트 번호 복사", null, delegate { CopyEntry(menuEntry); });
            var terminateItem = new ToolStripMenuItem("프로세스 종료…", null, delegate {
                var entry = menuEntry; if (entry == null || !entry.CanTerminate) return;
                Hide(); if (TerminateRequested != null) TerminateRequested(entry);
            });
            rowMenu.Items.AddRange(new ToolStripItem[] { openItem, copyItem, new ToolStripSeparator(), terminateItem });
            rowMenu.Opening += delegate {
                var entry = Selected();
                menuEntry = entry;
                openItem.Enabled = entry != null && entry.BrowserUrl != null;
                copyItem.Enabled = entry != null;
                terminateItem.Enabled = entry != null && entry.CanTerminate;
            };
            grid.ContextMenuStrip = rowMenu;
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0) grid.CurrentCell = grid.Rows[e.RowIndex].Cells[0];
            };
            feedbackTimer.Tick += delegate { feedbackTimer.Stop(); ResetFooter(); };
            search.TextChanged += delegate { ApplyFilter(true); };
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e) {
                if (updating || e.RowIndex < 0) return;
                if (grid.IsFavoriteColumn(e.ColumnIndex)) {
                    try { grid.ToggleFavorite(e.RowIndex, e.ColumnIndex, favorites); }
                    catch (Exception) { footer.Text = "즐겨찾기를 저장하지 못했습니다."; }
                }
                else CopySelected();
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) {
                var entry = Selected();
                if (e.RowIndex >= 0 && !grid.IsFavoriteColumn(e.ColumnIndex) && entry != null && entry.BrowserUrl != null && OpenBrowserRequested != null) OpenBrowserRequested(entry);
            };
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (grid.Focused && !e.Control && !e.Alt && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && grid.CurrentCell != null && grid.IsFavoriteColumn(grid.CurrentCell.ColumnIndex)) {
                    try { grid.ToggleFavorite(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, favorites); }
                    catch (Exception) { footer.Text = "즐겨찾기를 저장하지 못했습니다."; }
                    e.SuppressKeyPress = true; return;
                }
                if (e.KeyCode == Keys.Escape) { Hide(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Enter) { CopySelected(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.F5 && RefreshRequested != null) { RefreshRequested(); e.SuppressKeyPress = true; }
            };
            Deactivate += delegate { if (!rowMenu.Visible) Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            ResumeLayout(true);
        }

        internal void Open(PortSnapshot current)
        {
            UpdateSnapshot(current);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, area.Right - Width - 10), Math.Max(area.Top, area.Bottom - Height - 10));
            Show(); Activate(); search.Focus(); search.SelectAll();
            if (RefreshRequested != null) RefreshRequested();
        }
        internal void UpdateSnapshot(PortSnapshot value) { snapshot = value; ApplyFilter(false); }
        internal void ApplyTheme(ThemeColors next)
        {
            ThemePainter.Apply(this, colors, next);
            ThemePainter.ApplyMenu(rowMenu, next);
            colors = next;
            ThemePainter.ApplyTitleBar(this, next);
            Invalidate(true);
        }
        internal void RefreshFavorites() { signature = null; ApplyFilter(true); }
        private PortEntry Selected() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as PortEntry; }
        private void ApplyFilter(bool resetScroll)
        {
            var rows = PortView.FilterSort(snapshot.Entries, search.Text, "", 0, true, favorites);
            string next = String.Join("|", rows.Select(e => e.Key + e.ProcessName + e.BindingDisplay + e.ProcessStartedAtUtc + favorites.IsPortFavorite(e.Port) + ":" + favorites.IsProgramFavorite(e.ProcessName))) + ":" + snapshot.Error;
            if (signature == next && !resetScroll) return;
            signature = next;
            bool focused = search.Focused;
            var selected = Selected();
            string key = selected == null ? null : selected.Key;
            int scroll = resetScroll ? 0 : grid.FirstDisplayedScrollingRowIndex;
            updating = true;
            try {
                grid.Rows.Clear();
                foreach (var entry in rows) {
                    int row = grid.Rows.Add(entry.Port, entry.ProcessName, entry.Protocol);
                    grid.Rows[row].Tag = entry;
                    grid.SetFavoriteCells(grid.Rows[row], entry, favorites);
                }
                if (rows.Length > 0) {
                    int index = resetScroll || key == null ? 0 : Array.FindIndex(rows, e => e.Key == key);
                    if (index >= 0) grid.CurrentCell = grid.Rows[index].Cells[0];
                    else { grid.CurrentCell = null; grid.ClearSelection(); }
                    if (scroll >= 0) grid.FirstDisplayedScrollingRowIndex = Math.Min(scroll, rows.Length - 1);
                }
                empty.Visible = rows.Length == 0;
                if (empty.Visible) { empty.Text = String.IsNullOrEmpty(snapshot.Error) ? "검색 결과가 없습니다." : "포트 정보를 읽지 못했습니다."; empty.BringToFront(); }
                if (!feedbackTimer.Enabled) ResetFooter();
            }
            finally { updating = false; if (focused && ActiveForm == this) search.Focus(); }
        }
        private void ResetFooter() { footer.Text = "클릭해 포트 복사  ·  " + grid.Rows.Count + "개 항목"; }
        private void CopySelected()
        {
            CopyEntry(Selected());
        }
        private void CopyEntry(PortEntry entry)
        {
            if (entry == null) return;
            try { Clipboard.SetText(entry.Port.ToString()); footer.Text = entry.Port + " 포트를 복사했습니다."; }
            catch (System.Runtime.InteropServices.ExternalException) { footer.Text = "클립보드가 사용 중입니다."; }
            feedbackTimer.Stop(); feedbackTimer.Start();
        }
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ClassStyle |= 0x00020000; return value; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ThemePainter.ApplyTitleBar(this, colors ?? ThemeColors.Light); }
        protected override void Dispose(bool disposing) { if (disposing) { feedbackTimer.Dispose(); rowMenu.Dispose(); } base.Dispose(disposing); }
    }
}
