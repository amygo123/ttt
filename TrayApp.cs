
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.IO;

namespace StyleWatcherWin
{
    public static class Formatter
    {
        public static string Prettify(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Replace("\\n", "\n").Replace("\r\n", "\n");
            var lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
            s = string.Join("\n", lines);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }
    }

    public class TrayApp : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetFocus();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, ref int wParam, ref int lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, StringBuilder lParam);

        const int WM_GETTEXTLENGTH = 0x000E;
        const int WM_GETTEXT = 0x000D;
        const int EM_GETSEL = 0x00B0;
        const int WM_HOTKEY = 0x0312;

        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
        const int KEYEVENTF_KEYUP = 0x0002;
        const byte VK_MENU = 0x12;

        static void ReleaseAlt()
        {
            if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
        }

        readonly NotifyIcon _tray = new();
        readonly ContextMenuStrip _menu = new();
        readonly AppConfig _cfg;

        ResultForm? _window;
        readonly SemaphoreSlim _queryLock = new(1, 1);
        DateTime _lastHotkeyAt = DateTime.MinValue;

        int _hotkeyId = 1;
        uint _mod;
        uint _vk;
        bool _allowCloseAll = false;

        public TrayApp()
        {
            _cfg = AppConfig.Load() ?? new AppConfig();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Visible = false;

            _tray.Text = "随手查";
            try
            {
                var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) _tray.Icon = exeIcon;
                else
                {
                    var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
                    _tray.Icon = File.Exists(icoPath) ? new Icon(icoPath) : SystemIcons.Application;
                }
            }
            catch { _tray.Icon = SystemIcons.Application; }
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ToggleWindow(show: true);

            var itemToggle = new ToolStripMenuItem("显示/隐藏 窗口", null, (s, e) => ToggleWindow(toggle: true));
            var itemQuery = new ToolStripMenuItem("手动输入查询", null, (s, e) =>
            {
                EnsureWindow();
                var w = _window;
                if (w != null)
                {
                    w.FocusInput();
                    w.ShowNoActivateAtCursor();
                }
            });
            var itemConfig = new ToolStripMenuItem("打开配置文件", null, (s, e) =>
            {
                try { System.Diagnostics.Process.Start("notepad.exe", AppConfig.ConfigPath); } catch { }
            });
            var itemExit = new ToolStripMenuItem("退出", null, (s, e) => ExitApp());

            _menu.Items.Add(itemToggle);
            _menu.Items.Add(itemQuery);
            _menu.Items.Add(itemConfig);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(itemExit);
            _tray.ContextMenuStrip = _menu;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var hotkey = _cfg?.hotkey ?? "Alt+S";
            ParseHotkey(hotkey, out _mod, out _vk);
            if (!RegisterHotKey(Handle, _hotkeyId, _mod, _vk))
                MessageBox.Show($"热键 " + hotkey + " 注册失败，可能被占用。", "随手查", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            _tray.BalloonTipTitle = "随手查 已启动";
            _tray.BalloonTipText = $"选中文本后按 {hotkey} 查询；双击托盘可显示窗口。";
            _tray.ShowBalloonTip(2500);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                _ = OnHotkeyAsync();
            base.WndProc(ref m);
        }

        void EnsureWindow()
        {
            if (_window == null || _window.IsDisposed)
            {
                _window = new ResultForm(_cfg);
                _window.FormClosing += (s, e) =>
                {
                    if (!_allowCloseAll)
                    {
                        e.Cancel = true;
                        _window?.Hide();
                    }
                };
            }
        }

        void ToggleWindow(bool show = false, bool toggle = false)
        {
            EnsureWindow();
            var w = _window;
            if (w == null) return;

            if (toggle)
            {
                if (w.Visible) w.Hide();
                else w.ShowAndFocusCentered(_cfg.window.alwaysOnTop);
            }
            else if (show)
            {
                w.ShowAndFocusCentered(_cfg.window.alwaysOnTop);
            }
        }

        void ExitApp()
        {
            try { UnregisterHotKey(Handle, _hotkeyId, _mod, _vk); } catch { }
            _allowCloseAll = true;
            try { _window?.Close(); } catch { }
            _tray.Visible = false;
            Application.Exit();
        }

        private async Task OnHotkeyAsync()
        {
            ReleaseAlt(); // make sure Alt key state is up
            EnsureWindow();
            var w = _window;
            if (w == null) return;

            // Keep original behavior (selection -> API -> show window)
            await Task.Yield();
            w.ShowAndFocusCentered(_cfg.window.alwaysOnTop);
        }

        static void ParseHotkey(string hotkey, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(hotkey)) { mod = MOD_ALT; vk = (uint)Keys.S; return; }
            var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mod |= MOD_ALT;
                else if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) mod |= MOD_CONTROL;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mod |= MOD_SHIFT;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) mod |= MOD_WIN;
                else if (Enum.TryParse<Keys>(p, true, out var key)) vk = (uint)key;
            }
            if (vk == 0) vk = (uint)Keys.S;
        }
    }
}
