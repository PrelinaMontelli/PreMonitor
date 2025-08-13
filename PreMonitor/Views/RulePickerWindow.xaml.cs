using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace PreMonitor.Views
{
    public partial class RulePickerWindow : Window
    {
    // Apps tab removed
        public class ProcItem
        {
            public ImageSource? Icon { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public int Pid { get; set; }
            public string ExecutablePath { get; set; } = string.Empty;
        }
        public class WinItem
        {
            public ImageSource? Icon { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ProcessName { get; set; } = string.Empty;
            public int Pid { get; set; }
            public string ExecutablePath { get; set; } = string.Empty;
        }

    public IReadOnlyList<(string Name, string Exec)> SelectedItems { get; private set; } = Array.Empty<(string, string)>();

        public RulePickerWindow()
        {
            InitializeComponent();
            // 首次加载时弹出忙碌对话框并异步加载
            Loaded += async (s, e) => await WithBusyDialogAsync("请稍候，PreMonitor 正在整理程序列表…", RefreshAllAsync);
        }

    private async Task RefreshAllAsync()
        {
            try
            {
                await Task.Yield();
                var all = await Task.Run(() => Process.GetProcesses());

                // Processes
                var procs = await Task.Run(() =>
                    all.Select(p => new ProcItem
                    {
                        ProcessName = SafeGetName(p),
                        Pid = SafeGetPid(p),
                        ExecutablePath = SafeGetPath(p),
                        Icon = LoadIcon(SafeGetPath(p))
                    })
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .OrderBy(p => p.ProcessName)
                    .ThenBy(p => p.Pid)
                    .ToList());
                ProcsList.ItemsSource = procs;

                // Windows
                var wins = await Task.Run(() => EnumerateWindows());
                WinsList.ItemsSource = wins;
            }
            catch
            {
                // ignore
            }
        }

        private static string SafeGetName(Process? p)
        {
            try { return p?.ProcessName ?? string.Empty; } catch { return string.Empty; }
        }
        private static int SafeGetPid(Process? p)
        {
            try { return p?.Id ?? 0; } catch { return 0; }
        }
        private static string SafeGetPath(Process? p)
        {
            try { return p?.MainModule?.FileName ?? string.Empty; } catch { return string.Empty; }
        }

        private static ImageSource? LoadIcon(string? path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));
                if (bmp.CanFreeze) bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private List<WinItem> EnumerateWindows()
        {
            var list = new List<WinItem>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                var title = GetWindowText(hWnd);
                if (string.IsNullOrWhiteSpace(title)) return true;
                GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    var path = SafeGetPath(p);
                    list.Add(new WinItem
                    {
                        Title = title,
                        ProcessName = SafeGetName(p),
                        Pid = (int)pid,
                        ExecutablePath = path,
                        Icon = LoadIcon(path)
                    });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list.OrderBy(w => w.ProcessName).ThenBy(w => w.Title).ToList();
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var q = (FilterBox.Text ?? string.Empty).Trim();
            Predicate<object> pred = o => true;
            if (!string.IsNullOrEmpty(q))
            {
                pred = o =>
                {
                    return o.ToString()?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                        || (o is ProcItem pi && ($"{pi.ProcessName} {pi.Pid} {pi.ExecutablePath}").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (o is WinItem wi && ($"{wi.Title} {wi.ProcessName} {wi.Pid} {wi.ExecutablePath}").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                };
            }

            if (ProcsList.ItemsSource != null)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ProcsList.ItemsSource);
                view.Filter = o => pred(o);
                view.Refresh();
            }
            if (WinsList.ItemsSource != null)
            {
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(WinsList.ItemsSource);
                view.Filter = o => pred(o);
                view.Refresh();
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await WithBusyDialogAsync("请稍候，PreMonitor 正在整理程序列表…", RefreshAllAsync);
        }

        private async Task WithBusyDialogAsync(string message, Func<Task> work)
        {
            var dlg = new BusyDialog(message)
            {
                Owner = this,
                ShowInTaskbar = false
            };
            dlg.Show();
            try
            {
                // 让对话框先完成布局与动画启动
                await Dispatcher.Yield(DispatcherPriority.Render);
                await Task.Delay(50);
                await work();
            }
            finally
            {
                try { dlg.Close(); } catch { }
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnAccept(object? sender, RoutedEventArgs? e)
        {
            var chosen = new List<(string Name, string Exec)>();
            if (Tabs.SelectedIndex == 0)
            {
                foreach (var item in ProcsList.SelectedItems.OfType<ProcItem>())
                    if (!string.IsNullOrWhiteSpace(item.ProcessName))
                        chosen.Add((item.ProcessName, item.ExecutablePath ?? string.Empty));
            }
            else
            {
                foreach (var item in WinsList.SelectedItems.OfType<WinItem>())
                    if (!string.IsNullOrWhiteSpace(item.ProcessName))
                        chosen.Add((item.ProcessName, item.ExecutablePath ?? string.Empty));
            }
            if (chosen.Count > 0)
            {
                // 去重（按进程名）
                SelectedItems = chosen
                    .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                DialogResult = true;
                Close();
            }
        }

        #region Win32
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static string GetWindowText(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            var sb = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
        #endregion
    }
}
