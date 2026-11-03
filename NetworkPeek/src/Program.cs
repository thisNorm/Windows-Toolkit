using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetworkPeek
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var request = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WindowsToolkit.NetworkPeek.Show"))
            using (var mutex = new Mutex(true, "Local\\WindowsToolkit.NetworkPeek", out first))
            {
                if (!first)
                {
                    request.Set();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PeekContext());
            }
        }
    }

    internal sealed class PeekContext : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly CompactBar bar;
        private readonly DetailsWindow details;
        private readonly System.Windows.Forms.Timer timer;
        private readonly EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WindowsToolkit.NetworkPeek.Show");
        private readonly Preferences preferences = Preferences.Load();
        private readonly ToolStripMenuItem copyIp;
        private readonly ToolStripMenuItem copyGateway;
        private readonly ThemeSettings themeSettings = ThemeSettings.Load();
        private readonly ContextMenuStrip themePopup;
        private readonly List<ToolStripMenuItem> themeChoices = new List<ToolStripMenuItem>();
        private NetworkSnapshot snapshot = new NetworkSnapshot();
        private int requested = 1;
        private int ticks;
        private bool reading;
        private bool exiting;

        internal PeekContext()
        {
            bar = new CompactBar();
            details = new DetailsWindow();
            bar.OpenRequested += ShowDetails;
            bar.PositionChanged += delegate { preferences.Position = bar.Location; preferences.Save(); };
            bar.Location = preferences.Position ?? bar.DefaultPosition();
            bar.ClampToScreen();
            // Keep a UI-thread handle available even when both windows are hidden.
            IntPtr handle = bar.Handle;

            var menu = new ContextMenuStrip();
            menu.Items.Add("네트워크 정보 열기", null, delegate { ShowDetails(); });
            copyIp = new ToolStripMenuItem("IP 복사", null, delegate { if (snapshot.Primary != null) Copy(snapshot.Primary.IPv4); });
            copyGateway = new ToolStripMenuItem("게이트웨이 복사", null, delegate { if (snapshot.Primary != null) Copy(snapshot.Primary.Gateway); });
            menu.Items.Add(copyIp);
            menu.Items.Add(copyGateway);
            menu.Items.Add("전체 정보 복사", null, delegate { Copy(snapshot.ToReport()); });
            menu.Items.Add(new ToolStripSeparator());
            var visible = new ToolStripMenuItem("IP 표시줄 보이기") { CheckOnClick = true, Checked = preferences.BarVisible };
            visible.CheckedChanged += delegate
            {
                preferences.BarVisible = visible.Checked;
                if (visible.Checked) { bar.ClampToScreen(); bar.Show(); } else bar.Hide();
                preferences.Save();
            };
            menu.Items.Add(visible);
            var themeMenu = new ToolStripMenuItem("테마");
            themeMenu.DropDownItems.AddRange(CreateThemeItems());
            menu.Items.Add(themeMenu);
            themePopup = new ContextMenuStrip();
            themePopup.Items.AddRange(CreateThemeItems());
            details.MenuIsVisible = delegate { return menu.Visible || themePopup.Visible; };
            menu.Closed += OnMenuClosed;
            themePopup.Closed += OnMenuClosed;
            details.ThemeRequested += delegate(Point point) { themePopup.Show(point); };
            var startup = new ToolStripMenuItem("Windows 시작 시 실행");
            menu.Opening += delegate {
                try { startup.Checked = Startup.IsEnabled(); startup.Enabled = true; }
                catch (Exception) { startup.Enabled = false; }
            };
            startup.Click += delegate
            {
                try { Startup.SetEnabled(!Startup.IsEnabled()); startup.Checked = Startup.IsEnabled(); }
                catch (Exception error) { MessageBox.Show(error.Message, "시작 프로그램 설정", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            menu.Items.Add(startup);
            menu.Items.Add("새로 고침", null, delegate { Refresh(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), Text = "NetworkPeek · 네트워크 확인 중", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { if (details.Visible) details.Hide(); else ShowDetails(); } };
            bar.ContextMenuStrip = menu;
            details.ContextMenuStrip = menu;
            details.CopyRequested += Copy;
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
            themeSettings.Changed += OnThemeChanged;
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                if (showSignal.WaitOne(0)) ShowDetails();
                if (++ticks >= 3) { ticks = 0; Interlocked.Exchange(ref requested, 1); }
                if (Interlocked.Exchange(ref requested, 0) != 0) Refresh();
            };
            timer.Start();
            ApplyTheme();
            if (preferences.BarVisible) bar.Show();
            Refresh();
        }

        private void OnNetworkChanged(object sender, EventArgs e) { Interlocked.Exchange(ref requested, 1); }
        private ToolStripMenuItem[] CreateThemeItems()
        {
            var result = new List<ToolStripMenuItem>();
            foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System }) {
                ThemeMode selected = mode;
                var item = new ToolStripMenuItem(mode.ToString()) { Tag = mode };
                item.Click += delegate {
                    try { themeSettings.SetMode(selected); }
                    catch (Exception error) { MessageBox.Show(error.Message, "테마 설정", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                };
                themeChoices.Add(item); result.Add(item);
            }
            return result.ToArray();
        }
        private void OnThemeChanged(object sender, EventArgs e) { ApplyTheme(); }
        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (exiting || details.IsDisposed || !details.Visible) return;
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) details.Activate();
            else if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked || e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange) details.Hide();
        }
        private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (themeSettings.Mode != ThemeMode.System || exiting || bar.IsDisposed) return;
            try { bar.BeginInvoke((Action)delegate { if (!exiting) ApplyTheme(); }); }
            catch (InvalidOperationException) { }
        }
        private void ApplyTheme()
        {
            Theme.Current = themeSettings.IsDark ? ThemeColors.Dark : ThemeColors.Light;
            bar.ApplyTheme(); details.ApplyTheme();
            Theme.ApplyMenu(tray.ContextMenuStrip); Theme.ApplyMenu(themePopup);
            details.SetThemeMode(themeSettings.Mode);
            foreach (var item in themeChoices) item.Checked = (ThemeMode)item.Tag == themeSettings.Mode;
        }
        private void OnAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) { Interlocked.Exchange(ref requested, 1); }
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (!exiting && !bar.IsDisposed)
            {
                try { bar.BeginInvoke((Action)delegate { bar.ClampToScreen(); if (details.Visible) details.PlaceNear(bar.Visible ? bar : null); }); }
                catch (InvalidOperationException) { }
            }
        }

        private void Refresh()
        {
            if (reading || exiting) return;
            reading = true;
            Task.Factory.StartNew<NetworkSnapshot>(NetworkReader.Read).ContinueWith(delegate(Task<NetworkSnapshot> task)
            {
                if (exiting || bar.IsDisposed) return;
                try
                {
                    bar.BeginInvoke((Action)delegate
                    {
                        reading = false;
                        if (exiting) return;
                        snapshot = task.IsFaulted ? new NetworkSnapshot { Error = "네트워크 정보를 읽지 못했습니다.", CapturedAt = DateTime.Now } : task.Result;
                        bar.UpdateSnapshot(snapshot);
                        details.UpdateSnapshot(snapshot);
                        copyIp.Enabled = copyGateway.Enabled = snapshot.Primary != null;
                        tray.Text = snapshot.Primary == null ? "NetworkPeek · 기본 게이트웨이 연결 없음" : "NetworkPeek\nIP: " + snapshot.Primary.IPv4 + "\nGW: " + snapshot.Primary.Gateway;
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void ShowDetails()
        {
            details.UpdateSnapshot(snapshot);
            details.PlaceNear(bar.Visible ? bar : null);
            details.Show();
            details.Activate();
            Refresh();
        }

        private void Copy(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            try { Clipboard.SetText(value); details.ShowFeedback("클립보드에 복사했습니다."); }
            catch (System.Runtime.InteropServices.ExternalException) { details.ShowFeedback("클립보드가 사용 중입니다. 다시 눌러주세요."); }
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            timer.Stop();
            timer.Dispose();
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            themeSettings.Changed -= OnThemeChanged;
            tray.Visible = false;
            tray.Icon.Dispose();
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            themePopup.Dispose();
            details.Dispose();
            bar.Dispose();
            showSignal.Dispose();
            base.ExitThreadCore();
        }
    }

    internal sealed class Preferences
    {
        private static readonly string FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsToolkit", "NetworkPeek", "settings.txt");
        internal bool BarVisible = false;
        internal Point? Position;

        internal static Preferences Load()
        {
            var result = new Preferences();
            try
            {
                if (!File.Exists(FileName)) return result;
                var values = File.ReadAllLines(FileName);
                bool visible;
                int x, y;
                if (values.Length > 0 && Boolean.TryParse(values[0], out visible)) result.BarVisible = visible;
                if (values.Length > 2 && Int32.TryParse(values[1], out x) && Int32.TryParse(values[2], out y)) result.Position = new Point(x, y);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }

        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FileName));
                File.WriteAllLines(FileName, Position.HasValue ? new[] { BarVisible.ToString(), Position.Value.X.ToString(), Position.Value.Y.ToString() } : new[] { BarVisible.ToString() });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && String.Equals(key.GetValue("NetworkPeek") as string, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }
        internal static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue("NetworkPeek", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("NetworkPeek", false);
            }
        }
    }
}
