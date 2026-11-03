using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PortPeek
{
    internal sealed class PortGrid : DataGridView
    {
        private ThemeColors colors = ThemeColors.Light;
        internal PortGrid() { DoubleBuffered = true; }

        internal void ApplyTheme(ThemeColors next)
        {
            colors = next;
            BackgroundColor = colors.Background;
            // The getter can return a resolved copy when any default color is unset.
            var cells = DefaultCellStyle;
            cells.BackColor = colors.Background;
            cells.ForeColor = colors.Text;
            cells.SelectionBackColor = colors.Selection;
            cells.SelectionForeColor = colors.Text;
            DefaultCellStyle = cells;
            AlternatingRowsDefaultCellStyle.BackColor = colors.Alternate;
            var headers = ColumnHeadersDefaultCellStyle;
            headers.BackColor = headers.SelectionBackColor = colors.Header;
            headers.ForeColor = headers.SelectionForeColor = colors.Muted;
            ColumnHeadersDefaultCellStyle = headers;
            foreach (DataGridViewColumn column in Columns) {
                if (column.Name == "Port") column.DefaultCellStyle.ForeColor = column.DefaultCellStyle.SelectionForeColor = colors.Accent;
                if (column.Name == "Pid") { column.DefaultCellStyle.ForeColor = colors.Muted; column.DefaultCellStyle.SelectionForeColor = colors.Text; }
            }
            foreach (DataGridViewRow row in Rows) ApplyRowTheme(row);
            Invalidate();
        }

        private void ApplyRowTheme(DataGridViewRow row)
        {
            if (Columns.Contains("Protocol")) {
                var entry = row.Tag as PortEntry;
                bool udp = entry != null && entry.Protocol == "UDP";
                row.Cells["Protocol"].Style.ForeColor = udp ? colors.Udp : colors.Muted;
                row.Cells["Protocol"].Style.SelectionForeColor = udp ? colors.Udp : colors.Text;
            }
            foreach (string name in new[] { "FavoritePort", "FavoriteProgram" }) {
                if (!Columns.Contains(name)) continue;
                var cell = row.Cells[name];
                cell.Style.ForeColor = cell.Style.SelectionForeColor = String.Equals(cell.Value as string, "★") ? colors.Favorite : colors.EmptyStar;
            }
        }

        internal void AddFavoriteColumns()
        {
            foreach (string kind in new[] { "Port", "Program" }) {
                var column = new DataGridViewTextBoxColumn {
                    Name = "Favorite" + kind, HeaderText = "", Width = 34, MinimumWidth = 34,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                    SortMode = DataGridViewColumnSortMode.NotSortable, Resizable = DataGridViewTriState.False,
                    DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = new Padding(1, 0, 1, 0), Font = new Font("Segoe UI Symbol", 12), WrapMode = DataGridViewTriState.False }
                };
                column.HeaderCell.ToolTipText = kind == "Port" ? "포트 즐겨찾기" : "프로그램 즐겨찾기";
                Columns.Add(column);
                column.DisplayIndex = kind == "Port" ? 0 : 2;
            }
            CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e) { Cursor = e.RowIndex >= 0 && IsFavoriteColumn(e.ColumnIndex) ? Cursors.Hand : Cursors.Default; };
            CellMouseLeave += delegate { Cursor = Cursors.Default; };
        }

        internal bool IsFavoriteColumn(int column)
        {
            return column >= 0 && (Columns[column].Name == "FavoritePort" || Columns[column].Name == "FavoriteProgram");
        }

        internal void SetFavoriteCells(DataGridViewRow row, PortEntry entry, FavoriteStore favorites)
        {
            bool port = favorites.IsPortFavorite(entry.Port);
            bool canFavoriteProgram = FavoriteStore.CanFavoriteProgram(entry);
            bool program = canFavoriteProgram && favorites.IsProgramFavorite(entry.ProcessName);
            var portCell = row.Cells["FavoritePort"];
            var programCell = row.Cells["FavoriteProgram"];
            portCell.Value = port ? "★" : "☆";
            programCell.Value = canFavoriteProgram ? (program ? "★" : "☆") : "";
            portCell.ToolTipText = entry.Port + " 포트 즐겨찾기 " + (port ? "해제" : "추가");
            programCell.ToolTipText = canFavoriteProgram ? entry.ProcessName + "의 모든 포트 즐겨찾기 " + (program ? "해제" : "추가") : "프로그램명을 확인할 수 없습니다.";
            ApplyRowTheme(row);
        }

        internal void ToggleFavorite(int row, int column, FavoriteStore favorites)
        {
            var entry = Rows[row].Tag as PortEntry;
            if (entry == null) return;
            if (Columns[column].Name == "FavoritePort") {
                bool previous = favorites.IsPortFavorite(entry.Port);
                favorites.TogglePort(entry.Port);
                if (favorites.IsPortFavorite(entry.Port) == previous) throw new InvalidOperationException("즐겨찾기를 저장하지 못했습니다.");
            }
            else if (Columns[column].Name == "FavoriteProgram" && FavoriteStore.CanFavoriteProgram(entry)) {
                bool previous = favorites.IsProgramFavorite(entry.ProcessName);
                favorites.ToggleProgram(entry.ProcessName);
                if (favorites.IsProgramFavorite(entry.ProcessName) == previous) throw new InvalidOperationException("즐겨찾기를 저장하지 못했습니다.");
            }
        }
    }

    internal sealed class MainWindow : Form
    {
        private ThemeColors colors = ThemeColors.Light;
        private Color Ink { get { return colors.Text; } }
        private Color Muted { get { return colors.Muted; } }
        private Color Surface { get { return colors.Surface; } }
        private Color Blue { get { return colors.Accent; } }
        private readonly EventWaitHandle showSignal;
        private readonly TextBox search;
        private readonly RadioButton[] protocolButtons;
        private readonly PortGrid grid;
        private readonly Label empty;
        private readonly Label count;
        private readonly Label selection;
        private readonly Label status;
        private readonly CheckBox automatic;
        private readonly Button refresh;
        private readonly Button open;
        private readonly Button copy;
        private readonly Button terminate;
        private readonly ToolStripMenuItem menuOpen;
        private readonly ToolStripMenuItem menuCopy;
        private readonly ToolStripMenuItem menuTerminate;
        private readonly ContextMenuStrip rowMenu;
        private readonly NotifyIcon tray;
        private readonly QuickWindow quick;
        private readonly FavoriteStore favorites = FavoriteStore.Load();
        private readonly ThemeSettings themeSettings = ThemeSettings.Load();
        private readonly ContextMenuStrip themeMenu;
        private readonly Button themeButton;
        private readonly ToolStripMenuItem[] windowThemeItems;
        private readonly ToolStripMenuItem[] trayThemeItems;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ToolTip hints = new ToolTip();
        private PortSnapshot snapshot = new PortSnapshot();
        private string signature;
        private bool reading;
        private bool updating;
        private bool ending;
        private bool loaded;
        private bool startHidden;
        private int ticks;
        private int sortColumn;
        private bool ascending = true;
        private string protocolFilter = "";
        private DateTime feedbackUntil;

        internal MainWindow(EventWaitHandle showSignal, bool startInTray = false)
        {
            this.showSignal = showSignal;
            startHidden = startInTray;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "PortPeek";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Segoe UI", 10);
            ForeColor = Ink;
            BackColor = Color.White;
            ClientSize = new Size(1040, 680);
            MinimumSize = new Size(840, 500);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 14), RowCount = 5, ColumnCount = 1, BackColor = Color.White };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(layout);

            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "PortPeek", Font = new Font("Segoe UI", 20, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            count = new Label { Text = "포트 확인 중", AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            header.Controls.Add(count);
            themeMenu = new ContextMenuStrip();
            windowThemeItems = CreateThemeItems();
            themeMenu.Items.AddRange(windowThemeItems);
            themeButton = MakeButton("Theme · System", false, delegate { themeMenu.Show(themeButton, new Point(0, themeButton.Height)); });
            themeButton.Font = new Font("Segoe UI", 9);
            themeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            themeButton.AccessibleName = "테마 선택";
            header.Controls.Add(themeButton);
            header.Resize += delegate { PlaceThemeButton(); };
            layout.Controls.Add(header, 0, 0);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            var searchBox = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(12, 7, 12, 6), BackColor = Surface, Margin = new Padding(0, 0, 12, 0) };
            search = new ThemedSearchBox("포트, 프로그램, PID 검색  ·  Ctrl+F") { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Surface, Font = new Font("Segoe UI", 10), AccessibleName = "포트, 프로그램, PID 검색" };
            searchBox.Controls.Add(search);
            toolbar.Controls.Add(searchBox, 0, 0);
            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, BackColor = Surface, Padding = new Padding(2), Margin = new Padding(0, 0, 12, 0), AccessibleName = "프로토콜 필터" };
            protocolButtons = new RadioButton[3];
            string[] filterNames = { "전체", "TCP", "UDP" };
            for (int i = 0; i < filterNames.Length; i++) {
                var filter = new RadioButton { Text = filterNames[i], Tag = i == 0 ? "" : filterNames[i], Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(56, 32), Margin = Padding.Empty, Checked = i == 0, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9), AccessibleName = filterNames[i] + " 포트" };
                filter.FlatAppearance.BorderSize = 0;
                filter.FlatAppearance.CheckedBackColor = Color.FromArgb(226, 235, 253);
                filter.FlatAppearance.MouseOverBackColor = Color.FromArgb(234, 238, 245);
                filter.ForeColor = filter.Checked ? Blue : Muted;
                filter.CheckedChanged += delegate(object sender, EventArgs e) {
                    var selected = (RadioButton)sender;
                    selected.ForeColor = selected.Checked ? Blue : Muted;
                    if (selected.Checked) { protocolFilter = (string)selected.Tag; ApplyFilter(true); }
                };
                protocolButtons[i] = filter;
                filters.Controls.Add(filter);
            }
            toolbar.Controls.Add(filters, 1, 0);
            refresh = MakeButton("새로 고침", false, RefreshPorts);
            refresh.Dock = DockStyle.Top;
            refresh.Margin = Padding.Empty;
            toolbar.Controls.Add(refresh, 2, 0);
            layout.Controls.Add(toolbar, 0, 1);

            var listPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            grid = new PortGrid {
                Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AllowUserToOrderColumns = false, RowHeadersVisible = false,
                MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false, EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, GridColor = Color.FromArgb(242, 244, 247),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 38, AccessibleName = "사용 중인 포트 목록", TabIndex = 2
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle {
                Font = new Font("Segoe UI", 10), ForeColor = Ink, BackColor = Color.White,
                SelectionBackColor = Color.FromArgb(233, 240, 255), SelectionForeColor = Ink,
                Padding = new Padding(12, 0, 8, 0), WrapMode = DataGridViewTriState.False
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = new Font("Segoe UI", 9, FontStyle.Regular), ForeColor = Muted,
                BackColor = Color.FromArgb(247, 249, 252), SelectionBackColor = Color.FromArgb(247, 249, 252),
                Padding = new Padding(12, 0, 8, 0), WrapMode = DataGridViewTriState.False
            };
            grid.RowTemplate.Height = 38;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            AddColumn("Port", "포트", 104, 104);
            AddColumn("Process", "프로그램", 260, 160);
            AddColumn("Pid", "PID", 100, 85);
            AddColumn("Protocol", "유형", 106, 92);
            AddColumn("Binding", "바인딩 주소", 280, 190);
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[1].FillWeight = 45;
            grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[4].FillWeight = 55;
            grid.Columns[0].DefaultCellStyle.Font = new Font("Consolas", 11, FontStyle.Bold);
            grid.Columns[0].DefaultCellStyle.ForeColor = Blue;
            grid.Columns[2].DefaultCellStyle.ForeColor = Muted;
            grid.Columns[3].DefaultCellStyle.Font = new Font("Segoe UI", 9);
            grid.AddFavoriteColumns();
            listPanel.Controls.Add(grid);
            empty = new Label { Dock = DockStyle.Fill, Text = "포트 목록을 불러오는 중…", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, BackColor = Color.White };
            listPanel.Controls.Add(empty);
            layout.Controls.Add(listPanel, 0, 2);

            var actions = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 14, 0, 10) };
            selection = new Label { Dock = DockStyle.Fill, Text = "포트를 선택하세요", ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 16, 0) };
            actions.Controls.Add(selection);
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Dock = DockStyle.Right, Margin = Padding.Empty };
            open = MakeButton("브라우저 열기", false, OpenSelected);
            copy = MakeButton("포트 복사", false, CopySelected);
            terminate = MakeButton("프로세스 종료", true, TerminateSelected);
            buttons.Controls.Add(open); buttons.Controls.Add(copy); buttons.Controls.Add(terminate);
            actions.Controls.Add(buttons);
            layout.Controls.Add(actions, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            automatic = new CheckBox { Text = "자동 갱신", Checked = true, AutoSize = true, Dock = DockStyle.Right, ForeColor = Muted, Font = new Font("Segoe UI", 9) };
            status = new Label { Dock = DockStyle.Fill, Text = "TCP 대기 포트 · UDP 바인딩", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Font = new Font("Segoe UI", 9) };
            footer.Controls.Add(status); footer.Controls.Add(automatic);
            layout.Controls.Add(footer, 0, 4);

            rowMenu = new ContextMenuStrip();
            menuOpen = new ToolStripMenuItem("브라우저 열기", null, delegate { OpenSelected(); });
            menuCopy = new ToolStripMenuItem("포트 번호 복사", null, delegate { CopySelected(); });
            menuTerminate = new ToolStripMenuItem("프로세스 종료…", null, delegate { TerminateSelected(); });
            rowMenu.Items.AddRange(new ToolStripItem[] { menuOpen, menuCopy, new ToolStripSeparator(), menuTerminate });
            grid.ContextMenuStrip = rowMenu;
            rowMenu.Opening += delegate { UpdateActions(); };
            quick = new QuickWindow(favorites);
            quick.OpenFullRequested += RestoreWindow;
            quick.RefreshRequested += RefreshPorts;
            quick.OpenBrowserRequested += OpenEntry;
            quick.TerminateRequested += TerminateEntry;
            favorites.Changed += OnFavoritesChanged;
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("빠른 확인", null, delegate { ShowQuick(); });
            trayMenu.Items.Add("전체 목록 열기", null, delegate { RestoreWindow(); });
            var themeItem = new ToolStripMenuItem("테마");
            trayThemeItems = CreateThemeItems();
            themeItem.DropDownItems.AddRange(trayThemeItems);
            trayMenu.Items.Add(themeItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            var startup = new ToolStripMenuItem("Windows 시작 시 실행");
            trayMenu.Opening += delegate {
                try { startup.Checked = Startup.IsEnabled(); startup.Enabled = true; }
                catch (Exception) { startup.Enabled = false; }
            };
            startup.Click += delegate {
                try { Startup.SetEnabled(!Startup.IsEnabled()); startup.Checked = Startup.IsEnabled(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "시작 프로그램 설정", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            trayMenu.Items.Add(startup);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("종료", null, delegate { ending = true; Close(); });
            tray = new NotifyIcon { Icon = Icon, Text = "PortPeek", ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowQuick(); };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) RestoreWindow(); };

            search.TextChanged += delegate { ApplyFilter(true); };
            grid.SelectionChanged += delegate { if (!updating) UpdateActions(); };
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) {
                if (grid.IsFavoriteColumn(e.ColumnIndex)) return;
                ascending = e.ColumnIndex == sortColumn ? !ascending : true;
                sortColumn = e.ColumnIndex; ApplyFilter(false);
            };
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0) grid.CurrentCell = grid.Rows[e.RowIndex].Cells[0];
            };
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e) {
                if (!updating && e.RowIndex >= 0 && grid.IsFavoriteColumn(e.ColumnIndex)) {
                    try { grid.ToggleFavorite(e.RowIndex, e.ColumnIndex, favorites); }
                    catch (Exception) { Feedback("즐겨찾기를 저장하지 못했습니다. 다시 시도해 주세요."); }
                }
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && !grid.IsFavoriteColumn(e.ColumnIndex)) OpenSelected(); };
            KeyDown += OnKeyDown;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!ending && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            Shown += delegate {
                search.Focus(); RefreshPorts();
            };
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate {
                if (showSignal.WaitOne(0)) RestoreWindow();
                if (++ticks >= (Visible || quick.Visible ? 3 : 15)) { ticks = 0; if (automatic.Checked && !rowMenu.Visible) RefreshPorts(); }
                if (DateTime.Now >= feedbackUntil) UpdateStatus();
            };
            timer.Start();
            hints.SetToolTip(automatic, "목록을 3초마다 갱신합니다.");
            hints.SetToolTip(terminate, "선택한 프로세스가 사용 중인 모든 포트가 함께 닫힙니다.");
            themeSettings.Changed += OnThemeChanged;
            SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
            ApplyTheme();
            UpdateActions();
            ResumeLayout(true);
        }

        private Button MakeButton(string text, bool danger, Action action)
        {
            var button = new ThemedButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 36, Padding = new Padding(12, 6, 12, 6), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = danger ? Color.FromArgb(185, 51, 51) : Ink, Cursor = Cursors.Hand, Margin = new Padding(6, 0, 0, 0) };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 248, 251);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(232, 237, 245);
            button.Click += delegate { action(); };
            return button;
        }

        private ToolStripMenuItem[] CreateThemeItems()
        {
            return new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System }.Select(mode => {
                var item = new ToolStripMenuItem(mode.ToString()) { Tag = mode };
                item.Click += delegate {
                    try { themeSettings.SetMode(mode); }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "테마 설정", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                };
                return item;
            }).ToArray();
        }

        private void PlaceThemeButton()
        {
            if (themeButton == null || themeButton.Parent == null) return;
            themeButton.Location = new Point(Math.Max(0, themeButton.Parent.Width - themeButton.PreferredSize.Width), 7);
            count.Location = new Point(Math.Max(190, themeButton.Left - count.PreferredWidth - 18), 16);
        }

        private void OnThemeChanged(object sender, EventArgs e) { ApplyTheme(); }

        private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (themeSettings.Mode != ThemeMode.System || ending || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate { if (!ending) ApplyTheme(); }); }
            catch (InvalidOperationException) { }
        }

        private void ApplyTheme()
        {
            var next = themeSettings.IsDark ? ThemeColors.Dark : ThemeColors.Light;
            SuspendLayout();
            ThemePainter.Apply(this, colors, next);
            colors = next;
            quick.ApplyTheme(next);
            ThemePainter.ApplyMenu(rowMenu, next);
            ThemePainter.ApplyMenu(themeMenu, next);
            ThemePainter.ApplyMenu(tray.ContextMenuStrip, next);
            hints.BackColor = next.Surface;
            hints.ForeColor = next.Text;
            foreach (var item in windowThemeItems.Concat(trayThemeItems)) item.Checked = (ThemeMode)item.Tag == themeSettings.Mode;
            themeButton.Text = "Theme · " + themeSettings.Mode;
            PlaceThemeButton();
            ThemePainter.ApplyTitleBar(this, next);
            ResumeLayout(true);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ThemePainter.ApplyTitleBar(this, colors ?? ThemeColors.Light);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && value) {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                RefreshPorts();
                return;
            }
            base.SetVisibleCore(value);
        }

        private void AddColumn(string name, string caption, int width, int minimum)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = caption, Width = width, MinimumWidth = minimum, SortMode = DataGridViewColumnSortMode.Programmatic, Resizable = DataGridViewTriState.True });
        }

        private void RestoreWindow()
        {
            quick.Hide();
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); RefreshPorts();
        }

        private void ShowQuick()
        {
            if (quick.Visible) quick.Hide();
            else quick.Open(snapshot);
        }

        private void RefreshPorts()
        {
            if (reading || ending) return;
            reading = true;
            Task.Factory.StartNew<PortSnapshot>(PortReader.Read).ContinueWith(delegate(Task<PortSnapshot> task) {
                if (ending || IsDisposed) return;
                try { BeginInvoke((Action)delegate {
                    reading = false;
                    if (ending) return;
                    snapshot = task.IsFaulted ? new PortSnapshot { CapturedAt = DateTime.Now, Error = "포트 정보를 읽지 못했습니다." } : task.Result;
                    loaded = true; ApplyFilter(false); quick.UpdateSnapshot(snapshot);
                    tray.Text = "PortPeek\nTCP " + snapshot.Entries.Count(e => e.Protocol == "TCP") + " · UDP " + snapshot.Entries.Count(e => e.Protocol == "UDP") + "\n클릭: 빠른 확인 / 두 번: 전체 목록";
                }); }
                catch (InvalidOperationException) { }
            });
        }

        private PortEntry SelectedEntry()
        {
            return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as PortEntry;
        }

        private void OnFavoritesChanged(object sender, EventArgs e)
        {
            signature = null;
            ApplyFilter(true);
            quick.RefreshFavorites();
        }

        private void ApplyFilter(bool resetScroll)
        {
            if (!loaded) return;
            var rows = PortView.FilterSort(snapshot.Entries, search.Text, protocolFilter, sortColumn, ascending, favorites);
            string next = String.Join("|", rows.Select(e => e.Key + ":" + e.ProcessName + ":" + e.BindingDisplay + ":" + e.ProcessStartedAtUtc + ":" + favorites.IsPortFavorite(e.Port) + ":" + favorites.IsProgramFavorite(e.ProcessName))) + ":" + sortColumn + ":" + ascending + ":" + snapshot.Error;
            count.Text = rows.Length == snapshot.Entries.Length ? rows.Length + "개의 포트" : rows.Length + " / " + snapshot.Entries.Length + "개의 포트";
            PlaceThemeButton();
            if (next == signature && !resetScroll) { UpdateStatus(); return; }
            signature = next;
            var selected = SelectedEntry();
            bool restoreSearch = search.Focused;
            var restoreProtocol = protocolButtons.FirstOrDefault(button => button.Focused);
            string key = selected == null ? null : selected.Key;
            int scroll = !resetScroll && grid.FirstDisplayedScrollingRowIndex >= 0 ? grid.FirstDisplayedScrollingRowIndex : 0;
            updating = true;
            grid.SuspendLayout();
            try {
                grid.Rows.Clear();
                foreach (var entry in rows) {
                    int index = grid.Rows.Add(entry.Port, entry.ProcessName, entry.ProcessId, entry.Protocol, entry.BindingDisplay);
                    var row = grid.Rows[index]; row.Tag = entry;
                    row.Cells[4].ToolTipText = String.Join(", ", entry.Addresses);
                    grid.SetFavoriteCells(row, entry, favorites);
                }
                foreach (DataGridViewColumn column in grid.Columns) column.HeaderCell.SortGlyphDirection = SortOrder.None;
                grid.Columns[sortColumn].HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
                if (rows.Length > 0) {
                    int index = resetScroll || key == null ? 0 : Array.FindIndex(rows, e => e.Key == key);
                    if (index >= 0) grid.CurrentCell = grid.Rows[index].Cells[0];
                    else { grid.CurrentCell = null; grid.ClearSelection(); }
                    grid.FirstDisplayedScrollingRowIndex = Math.Min(scroll, rows.Length - 1);
                }
                empty.Visible = rows.Length == 0;
                if (empty.Visible) {
                    empty.Text = snapshot.Entries.Length > 0 ? "검색 결과가 없습니다.\r\n검색어 또는 프로토콜 필터를 바꿔보세요." : String.IsNullOrEmpty(snapshot.Error) ? "사용 중인 TCP / UDP 포트가 없습니다." : "포트 목록을 읽지 못했습니다.\r\n새로 고침을 눌러 다시 시도하세요.";
                    empty.BringToFront();
                }
            }
            finally {
                updating = false; grid.ResumeLayout();
                if (ActiveForm == this) {
                    if (restoreSearch) search.Focus();
                    else if (restoreProtocol != null) restoreProtocol.Focus();
                }
            }
            UpdateActions(); UpdateStatus();
        }

        private void UpdateActions()
        {
            var entry = SelectedEntry();
            open.Enabled = menuOpen.Enabled = entry != null && entry.BrowserUrl != null;
            copy.Enabled = menuCopy.Enabled = entry != null;
            terminate.Enabled = menuTerminate.Enabled = entry != null && entry.CanTerminate;
            selection.Text = entry == null ? "포트를 선택하세요" : entry.ProcessName + "  ·  PID " + entry.ProcessId;
            hints.SetToolTip(selection, entry == null ? "" : entry.Protocol + " " + entry.Port + "\r\n" + String.Join(", ", entry.Addresses));
        }

        private void UpdateStatus()
        {
            if (DateTime.Now < feedbackUntil) return;
            status.ForeColor = Muted;
            status.Text = !String.IsNullOrEmpty(snapshot.Error) ? snapshot.Error : !loaded ? "TCP 대기 포트 · UDP 바인딩" : "TCP 대기 포트 · UDP 바인딩  /  " + snapshot.CapturedAt.ToString("HH:mm:ss") + " 갱신";
            hints.SetToolTip(status, snapshot.Error ?? "열 제목을 누르면 정렬합니다. TCP 항목을 두 번 누르면 브라우저로 엽니다.");
        }

        private void Feedback(string text)
        {
            feedbackUntil = DateTime.Now.AddSeconds(3);
            status.Text = text; status.ForeColor = Blue;
        }

        private void CopySelected()
        {
            var entry = SelectedEntry(); if (entry == null) return;
            try { Clipboard.SetText(entry.Port.ToString()); Feedback(entry.Port + " 포트를 복사했습니다."); }
            catch (System.Runtime.InteropServices.ExternalException) { Feedback("클립보드가 사용 중입니다. 다시 눌러주세요."); }
        }

        private void OpenSelected()
        {
            OpenEntry(SelectedEntry());
        }

        private void OpenEntry(PortEntry entry)
        {
            if (entry == null || entry.BrowserUrl == null) return;
            try { Process.Start(new ProcessStartInfo(entry.BrowserUrl) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "브라우저 열기", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private void TerminateSelected()
        {
            TerminateEntry(SelectedEntry());
        }

        private void TerminateEntry(PortEntry entry)
        {
            if (entry == null || !entry.CanTerminate) return;
            bool previous = automatic.Checked; automatic.Checked = false;
            try {
                var answer = MessageBox.Show(this, entry.ProcessName + " (PID " + entry.ProcessId + ")를 종료할까요?\r\n\r\n이 프로그램이 사용하는 다른 포트도 함께 닫힙니다.\r\n저장하지 않은 작업은 잃을 수 있습니다.", "프로세스 종료", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
                PortActions.Terminate(entry); Feedback(entry.ProcessName + "에 종료를 요청했습니다.");
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "프로세스를 종료하지 못했습니다", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { automatic.Checked = previous; RefreshPorts(); }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (grid.Focused && !e.Control && !e.Alt && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && grid.CurrentCell != null && grid.IsFavoriteColumn(grid.CurrentCell.ColumnIndex)) {
                try { grid.ToggleFavorite(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, favorites); }
                catch (Exception) { Feedback("즐겨찾기를 저장하지 못했습니다. 다시 시도해 주세요."); }
                e.SuppressKeyPress = true; return;
            }
            if (e.Control && e.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.F5) { RefreshPorts(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.Space) { ShowQuick(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape && search.TextLength > 0) { search.Clear(); e.SuppressKeyPress = true; }
            if (grid.Focused && e.Control && e.KeyCode == Keys.C) { CopySelected(); e.SuppressKeyPress = true; }
            if (grid.Focused && e.KeyCode == Keys.Enter) { OpenSelected(); e.SuppressKeyPress = true; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ending = true;
            favorites.Changed -= OnFavoritesChanged;
            themeSettings.Changed -= OnThemeChanged;
            SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            timer.Stop(); timer.Dispose(); tray.Visible = false;
            tray.ContextMenuStrip.Dispose(); tray.Dispose(); rowMenu.Dispose(); themeMenu.Dispose(); hints.Dispose(); quick.Dispose();
            base.OnFormClosed(e);
        }

    }
}
