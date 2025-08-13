using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Drawing;
using Microsoft.Extensions.DependencyInjection;

namespace PreMonitor.Services
{
    public interface ITrayService
    {
        /// <summary>
        /// 初始化托盘图标
        /// </summary>
        void InitializeTray();

        /// <summary>
        /// 显示托盘图标
        /// </summary>
        void ShowTray();

        /// <summary>
        /// 隐藏托盘图标
        /// </summary>
        void HideTray();

        /// <summary>
        /// 显示托盘通知
        /// </summary>
        void ShowNotification(string title, string message);

        /// <summary>
        /// 更新托盘状态
        /// </summary>
        /// <param name="isMonitoring">是否正在监控</param>
        void UpdateTrayStatus(bool isMonitoring);

        /// <summary>
        /// 释放托盘资源
        /// </summary>
        void Dispose();
    }

    public class TrayService : ITrayService, IDisposable
    {
        private readonly ILogger<TrayService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private TaskbarIcon? _taskbarIcon;
        private bool _disposed = false;

        public TrayService(ILogger<TrayService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public void InitializeTray()
        {
            try
            {
                _taskbarIcon = new TaskbarIcon();
                
                // 设置托盘图标 - 使用应用程序图标
                try
                {
                    // 方法1：尝试从应用程序主程序集获取图标
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    // 尝试新的命名空间资源名，其次兼容旧名
                    var iconStream = assembly.GetManifestResourceStream("PreMonitor.icon.ico")
                                     ?? assembly.GetManifestResourceStream("EdgeMonitor.icon.ico");
                    
                    if (iconStream != null)
                    {
                        _taskbarIcon.Icon = new System.Drawing.Icon(iconStream);
                        _logger.LogInformation("托盘图标从嵌入资源加载成功");
                    }
                    else
                    {
                        // 方法2：尝试从应用程序当前目录加载
                        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                        if (File.Exists(iconPath))
                        {
                            _taskbarIcon.Icon = new System.Drawing.Icon(iconPath);
                            _logger.LogInformation($"托盘图标从文件路径加载成功: {iconPath}");
                        }
                        else
                        {
                            // 方法3：尝试使用应用程序的图标
                            var appIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                            if (appIcon != null)
                            {
                                _taskbarIcon.Icon = appIcon;
                                _logger.LogInformation("托盘图标从应用程序图标加载成功");
                            }
                            else
                            {
                                // 方法4：使用系统默认应用程序图标
                                _taskbarIcon.Icon = SystemIcons.Application;
                                _logger.LogWarning("使用系统默认图标，未找到自定义图标文件");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"加载托盘图标失败: {ex.Message}，使用系统默认图标");
                    try
                    {
                        _taskbarIcon.Icon = SystemIcons.Application;
                    }
                    catch
                    {
                        _logger.LogError("连系统默认图标都无法加载");
                    }
                }
                
                _taskbarIcon.ToolTipText = "PreMonitor - 就绪";
                
                // 双击托盘图标显示主窗口
                _taskbarIcon.TrayMouseDoubleClick += (s, e) =>
                {
                    ShowMainWindow();
                };
                
                // 单击托盘图标也显示主窗口
                _taskbarIcon.TrayLeftMouseUp += (s, e) =>
                {
                    ShowMainWindow();
                };

                // 创建右键菜单
                var contextMenu = new System.Windows.Controls.ContextMenu();
                
                var showMenuItem = new System.Windows.Controls.MenuItem
                {
                    Header = "显示主窗口"
                };
                showMenuItem.Click += (s, e) => ShowMainWindow();
                
                var exitMenuItem = new System.Windows.Controls.MenuItem
                {
                    Header = "退出程序"
                };
                exitMenuItem.Click += (s, e) => ExitApplication();
                
                contextMenu.Items.Add(showMenuItem);
                contextMenu.Items.Add(new System.Windows.Controls.Separator());
                contextMenu.Items.Add(exitMenuItem);
                
                _taskbarIcon.ContextMenu = contextMenu;
                
                _logger.LogInformation("托盘图标已初始化");
            }
            catch (Exception ex)
            {
                _logger.LogError($"初始化托盘图标失败: {ex.Message}");
            }
        }

        public void ShowTray()
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.Visibility = Visibility.Visible;
                _logger.LogInformation("托盘图标已显示");
            }
        }

        public void HideTray()
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.Visibility = Visibility.Hidden;
                _logger.LogInformation("托盘图标已隐藏");
            }
        }

        public void ShowNotification(string title, string message)
        {
            if (_taskbarIcon != null)
            {
                _taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
                _logger.LogInformation($"显示托盘通知: {title} - {message}");
            }
        }

        public void UpdateTrayStatus(bool isMonitoring)
        {
            if (_taskbarIcon != null)
            {
                var statusText = isMonitoring ? "PreMonitor - 监控中" : "PreMonitor - 就绪";
                _taskbarIcon.ToolTipText = statusText;
                _logger.LogInformation($"托盘状态已更新: {statusText}");
            }
        }

        private void ShowMainWindow()
        {
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                
                // 如果主窗口不存在，创建一个新的
                if (mainWindow == null && Application.Current != null)
                {
                    _logger.LogInformation("主窗口不存在，正在创建新的主窗口");
                    mainWindow = _serviceProvider.GetRequiredService<PreMonitor.MainWindow>();
                    Application.Current.MainWindow = mainWindow;
                }
                
                if (mainWindow != null)
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    mainWindow.Topmost = true;  // 确保窗口置顶
                    mainWindow.Topmost = false; // 然后取消置顶，让它正常显示
                    _logger.LogInformation("主窗口已从托盘恢复");
                }
                else
                {
                    _logger.LogError("无法创建或获取主窗口");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "显示主窗口时发生错误");
            }
        }

        private void ExitApplication()
        {
            _logger.LogInformation("从托盘退出应用程序");
            Application.Current?.Shutdown();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _taskbarIcon?.Dispose();
                _disposed = true;
                _logger.LogInformation("托盘服务已释放");
            }
        }
    }
}
