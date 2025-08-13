using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Windows;
using PreMonitor.ViewModels;
using PreMonitor.Services;
using System.IO;

namespace PreMonitor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _host;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<IPrivilegeService, PrivilegeService>();
                    services.AddSingleton<IEdgeMonitorService, EdgeMonitorService>();
                    services.AddSingleton<IDataService, DataService>();
                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<ILogService, LogService>();
                    services.AddSingleton<ITrayService, TrayService>();
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<IStartupService, StartupService>();
                    services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // 启动服务
                await _host.StartAsync();

                // 检查是否是托盘监测模式
                bool isTrayMonitorMode = e.Args.Contains("--tray-monitor");

                // 检查管理员权限
                var privilegeService = _host.Services.GetRequiredService<IPrivilegeService>();
                if (!privilegeService.IsRunningAsAdministrator())
                {
                    if (!isTrayMonitorMode)
                    {
                        privilegeService.ShowAdministratorRequiredMessage();
                    }
                    Shutdown();
                    return;
                }

                // 执行启动检查
                var startupService = _host.Services.GetRequiredService<IStartupService>();
                startupService.StartupCheck();

                // 预加载 PreMonitor 设置（里程碑1）
                var configService = _host.Services.GetRequiredService<IConfigurationService>();
                var preSettings = configService.LoadPreMonitorSettings();
                // 暂不绑定到 UI，后续里程碑接入规则管理

                // 获取托盘服务
                var trayService = _host.Services.GetRequiredService<ITrayService>();
                
                // 初始化托盘服务
                trayService.InitializeTray();

                if (isTrayMonitorMode)
                {
                    // 托盘监测模式不显示主窗口自动开始监测
                    var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
                    
                    // 确保托盘显示
                    trayService.ShowTray();
                    
                    // 启动托盘监测模式
                    mainViewModel.StartTrayMonitoring();
                    
                    // 在托盘模式下，并不设置MainWindow
                }
                else
                {
                    // 正常模式显示主窗口
                    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    mainWindow.Show();
                    
                    // 设置为应用程序的主窗口
                    MainWindow = mainWindow;
                }

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用程序启动时发生严重错误: {ex.Message}\n\n{ex.ToString()}", 
                                "启动失败", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                // 保存 PreMonitor 设置（如果未来在运行时有更改）
                var configService = _host.Services.GetRequiredService<IConfigurationService>();
                // 目前没有全局状态持有 settings，对应里程碑1先读后写一次（幂等）
                var settings = configService.LoadPreMonitorSettings();
                await configService.SavePreMonitorSettingsAsync(settings);
            }
            catch { }
            await _host.StopAsync();
            _host.Dispose();

            base.OnExit(e);
        }
    }
}
