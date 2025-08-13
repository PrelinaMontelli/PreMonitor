using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using PreMonitor.Services;
using PreMonitor.Commands;
using PreMonitor.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace PreMonitor.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly IDataService _dataService;
        private readonly IDialogService _dialogService;
        private readonly IPrivilegeService _privilegeService;
        private readonly IEdgeMonitorService _edgeMonitorService;
    private readonly ILogService _logService;
        private readonly IConfigurationService _configService;
        
        private readonly IStartupService _startupService;
        private readonly ITrayService _trayService;
    private readonly IProcessMonitorService _processMonitorService;
    // 规则超限开始时间缓存（按进程名聚合）
    private readonly Dictionary<string, DateTime> _exceedSince = new(StringComparer.OrdinalIgnoreCase);
        
        private string _statusMessage = "就绪";
        private int _monitorInterval = 5;
        private bool _autoSaveEnabled = true;
        private DateTime _currentTime = DateTime.Now;
    private string _windowTitle = "PreMonitor";
        private bool _isMonitoring = false;
        private System.Windows.Threading.DispatcherTimer? _monitorTimer;
        private bool _isCurrentlyMonitoring = false; // 防止重叠检查
        private CloseAction _closeAction = CloseAction.Ask;
        private bool _isStartupEnabled = false;
        private bool _isTrayMonitorStartupEnabled = false;
        
        // Milestone1: settings & rules
        private PreMonitorSettings _settings = new PreMonitorSettings();
        public ObservableCollection<ApplicationRule> ApplicationRules { get; } = new();
        private ApplicationRule? _selectedRule;
        public ApplicationRule? SelectedRule
        {
            get => _selectedRule;
            set { _selectedRule = value; OnPropertyChanged(); }
        }
        public Thresholds GlobalThresholds
        {
            get => _settings.GlobalThresholds;
            set { _settings.GlobalThresholds = value; OnPropertyChanged(); }
        }
        
        public MainViewModel(
            ILogger<MainViewModel> logger,
            IDataService dataService,
            IDialogService dialogService,
            IPrivilegeService privilegeService,
            IEdgeMonitorService edgeMonitorService,
            ILogService logService,
            IConfigurationService configService,
            IStartupService startupService,
            ITrayService trayService,
            IProcessMonitorService processMonitorService)
        {
            _logger = logger;
            _dataService = dataService;
            _dialogService = dialogService;
            _privilegeService = privilegeService;
            _edgeMonitorService = edgeMonitorService;
            _logService = logService;
            _configService = configService;
            _startupService = startupService;
            _trayService = trayService;
            _processMonitorService = processMonitorService;
            
            // 订阅日志集合变化事件
            _logService.LogEntries.CollectionChanged += (s, e) => NotifyLogPropertiesChanged();
            _logService.MonitorEntries.CollectionChanged += (s, e) => NotifyLogPropertiesChanged();
            
            InitializeCommands();
            StartTimeUpdater();
            UpdateWindowTitle();
            LoadCloseActionSettings();
            LoadStartupSettings();
            // 载入 PreMonitor 设置（规则/阈值）
            try
            {
                var settings = _configService.LoadPreMonitorSettings();
                _settings = settings;
                ApplicationRules.Clear();
                foreach (var r in settings.Rules)
                    ApplicationRules.Add(r);
                OnPropertyChanged(nameof(GlobalThresholds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载 PreMonitor 设置失败");
            }
            
            // 启动时清理过期日志文件
            _ = Task.Run(async () => await _logService.CleanupOldLogFilesAsync());
        }

        #region Properties

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string MonitorData
        {
            get => string.Join("\n", _logService.MonitorEntries);
        }

        public string LogData
        {
            get => string.Join("\n", _logService.LogEntries);
        }

        /// <summary>
        /// 通知日志属性已更改
        /// </summary>
        private void NotifyLogPropertiesChanged()
        {
            OnPropertyChanged(nameof(MonitorData));
            OnPropertyChanged(nameof(LogData));
        }

        public int MonitorInterval
        {
            get => _monitorInterval;
            set 
            {
                // 验证输入值，确保在合理范围内
                var newValue = Math.Max(1, Math.Min(3600, value)); // 限制在1-3600秒之间
                if (SetProperty(ref _monitorInterval, newValue))
                {
                    // 如果监控正在运行，更新定时器间隔
                    if (IsMonitoring && _monitorTimer != null)
                    {
                        _monitorTimer.Interval = TimeSpan.FromSeconds(_monitorInterval);
                        _logger.LogInformation($"监控间隔已更新为: {_monitorInterval}秒");
                    }
                }
            }
        }

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => SetProperty(ref _autoSaveEnabled, value);
        }

        public DateTime CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(ref _isMonitoring, value);
        }

        public CloseAction CloseAction
        {
            get => _closeAction;
            set 
            {
                if (SetProperty(ref _closeAction, value))
                {
                    // 异步保存关闭行为设置
                    _ = Task.Run(async () => await SaveCloseActionAsync(value));
                }
            }
        }

        public bool IsStartupEnabled
        {
            get => _isStartupEnabled;
            set 
            {
                if (SetProperty(ref _isStartupEnabled, value))
                {
                    // 如果启用开机自启，则禁用托盘监测启动
                    if (value && _isTrayMonitorStartupEnabled)
                    {
                        _isTrayMonitorStartupEnabled = false;
                        OnPropertyChanged(nameof(IsTrayMonitorStartupEnabled));
                    }
                    // 异步调用启动服务
                    _ = Task.Run(async () => await UpdateStartupStatusAsync(value));
                }
            }
        }

        public bool IsTrayMonitorStartupEnabled
        {
            get => _isTrayMonitorStartupEnabled;
            set 
            {
                if (SetProperty(ref _isTrayMonitorStartupEnabled, value))
                {
                    // 如果启用托盘监测启动，则禁用开机自启
                    if (value && _isStartupEnabled)
                    {
                        _isStartupEnabled = false;
                        OnPropertyChanged(nameof(IsStartupEnabled));
                    }
                    // 异步调用托盘监测启动服务
                    _ = Task.Run(async () => await UpdateTrayMonitorStartupStatusAsync(value));
                }
            }
        }

        #endregion

        #region Commands

        public ICommand AboutCommand { get; private set; } = null!;
        public ICommand StartMonitoringCommand { get; private set; } = null!;
        public ICommand StopMonitoringCommand { get; private set; } = null!;
        public ICommand ClearLogsCommand { get; private set; } = null!;
        public ICommand CheckAdminCommand { get; private set; } = null!;
        public ICommand TestEdgeDetectionCommand { get; private set; } = null!;
        public ICommand ForceKillEdgeCommand { get; private set; } = null!;
        public ICommand ViewLogStatsCommand { get; private set; } = null!;
        public ICommand OpenLogFolderCommand { get; private set; } = null!;
        public ICommand ResetCloseChoiceCommand { get; private set; } = null!;
    public ICommand AddRuleCommand { get; private set; } = null!;
    public ICommand RemoveSelectedRuleCommand { get; private set; } = null!;
    public ICommand SaveSettingsCommand { get; private set; } = null!;

        #endregion

        private void InitializeCommands()
        {
            AboutCommand = new RelayCommand(ExecuteAbout);
            StartMonitoringCommand = new RelayCommand(ExecuteStartMonitoring);
            StopMonitoringCommand = new RelayCommand(ExecuteStopMonitoring);
            ClearLogsCommand = new RelayCommand(ExecuteClearLogs);
            CheckAdminCommand = new RelayCommand(ExecuteCheckAdmin);
            TestEdgeDetectionCommand = new RelayCommand(ExecuteTestEdgeDetection);
            ForceKillEdgeCommand = new RelayCommand(ExecuteForceKillEdge);
            ViewLogStatsCommand = new RelayCommand(ExecuteViewLogStats);
            OpenLogFolderCommand = new RelayCommand(ExecuteOpenLogFolder);
            ResetCloseChoiceCommand = new RelayCommand(ExecuteResetCloseChoice);
            AddRuleCommand = new RelayCommand(ExecuteAddRule);
            RemoveSelectedRuleCommand = new RelayCommand(ExecuteRemoveSelectedRule, () => SelectedRule != null);
            SaveSettingsCommand = new RelayCommand(ExecuteSaveSettings);
        }

        private void StartTimeUpdater()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) => CurrentTime = DateTime.Now;
            timer.Start();
        }

        private void UpdateWindowTitle()
        {
            var baseTitle = "PreMonitor";
            if (_privilegeService.IsRunningAsAdministrator())
            {
                WindowTitle = $"{baseTitle} - 管理员";
                _logger.LogInformation("应用程序正在以管理员权限运行");
            }
            else
            {
                WindowTitle = baseTitle;
                _logger.LogWarning("应用程序未以管理员权限运行");
            }
        }

        #region Command Implementations

        private void ExecuteAbout()
        {
            _logger.LogInformation("显示关于对话框");
            var aboutWindow = new PreMonitor.Views.AboutWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            aboutWindow.ShowDialog();
        }

        private async void ExecuteStartMonitoring()
        {
            if (IsMonitoring)
            {
                _logger.LogWarning("监控已在运行中");
                return;
            }

            _logger.LogInformation("开始监控");
            IsMonitoring = true;
            StatusMessage = "监控已启动";
            
            // 更新托盘状态
            _trayService.UpdateTrayStatus(true);
            
            var message = $"监控已启动 - 检查间隔: {MonitorInterval}秒";
            await _logService.AddMonitorEntryAsync(message);
            await _logService.AddLogEntryAsync("INFO: 监控服务已启动");

            // 启动监控定时器
            _monitorTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(MonitorInterval)
            };
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            // 立即执行一次检查（同步方式，避免定时器冲突）
            _logger.LogInformation("立即执行首次检查");
            _ = Task.Run(async () => 
            {
                await Task.Delay(1000); // 短暂延迟让界面更新
                await PerformRulesMonitoringAsync();
            });
        }

        private async void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            // 防止重叠检查
            if (_isCurrentlyMonitoring)
            {
                _logger.LogWarning("上一次监控检查尚未完成，跳过本次检查");
                return;
            }

            _logger.LogInformation("定时器触发监控检查");
            await PerformRulesMonitoringAsync();
        }

        private async void ExecuteStopMonitoring()
        {
            if (!IsMonitoring)
            {
                _logger.LogWarning("监控未在运行");
                return;
            }

            _logger.LogInformation("停止监控");
            IsMonitoring = false;
            StatusMessage = "监控已停止";
            
            // 更新托盘状态
            _trayService.UpdateTrayStatus(false);
            
            _monitorTimer?.Stop();
            _monitorTimer = null;
            
            var message = "监控已停止";
            await _logService.AddMonitorEntryAsync(message);
            await _logService.AddLogEntryAsync("INFO: 监控服务已停止");
        }

        /// <summary>
        /// 启动托盘监测模式
        /// </summary>
        public async void StartTrayMonitoring()
        {
            try
            {
                _logger.LogInformation("启动托盘监测模式");
                
                // 直接开始监测，不显示窗口
                await Task.Delay(2000); // 等待系统完全启动
                ExecuteStartMonitoring();
                
                _logger.LogInformation("托盘监测模式已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动托盘监测模式时发生错误");
            }
        }

        private void ExecuteClearLogs()
        {
            _logger.LogInformation("清除内存日志");
            StatusMessage = "内存日志已清除";
            _logService.ClearMemoryLogs();
        }

        private void ExecuteCheckAdmin()
        {
            var isAdmin = _privilegeService.IsRunningAsAdministrator();
            var status = isAdmin ? "是" : "否";
            _dialogService.ShowMessage("管理员权限检查", $"当前是否以管理员身份运行: {status}");
            
            if (!isAdmin)
            {
                var result = _dialogService.ShowConfirmation("权限提升", "是否要以管理员身份重新启动程序？");
                if (result)
                {
                    _privilegeService.RestartAsAdministrator();
                }
            }
        }

        private async void ExecuteTestEdgeDetection()
        {
            _logger.LogInformation("手动执行检测测试");
            StatusMessage = "正在执行Edge检测测试...";
            
            try
            {
                var edgeProcesses = await _edgeMonitorService.GetEdgeProcessesAsync();
                var hasVisibleWindows = await _edgeMonitorService.HasVisibleWindowsAsync();
                var hasAbnormalUsage = _edgeMonitorService.HasAbnormalResourceUsage(edgeProcesses, 30.0, 2048);
                
                var totalCpu = edgeProcesses.Sum(p => p.CpuUsage);
                var totalMemory = edgeProcesses.Sum(p => p.MemoryUsageMB);
                
                var testResult = $"=== 检测测试结果 ===\n" +
                               $"进程数量: {edgeProcesses.Length}\n" +
                               $"总CPU使用: {totalCpu:F1}%\n" +
                               $"总内存使用: {totalMemory}MB\n" +
                               $"有可见窗口: {hasVisibleWindows}\n" +
                               $"资源异常: {hasAbnormalUsage}\n" +
                               $"满足终止条件: {!hasVisibleWindows && hasAbnormalUsage}\n" +
                               $"==================";
                
                await _logService.AddLogEntryAsync(testResult);
                
                foreach (var process in edgeProcesses)
                {
                    var processInfo = $"进程 {process.ProcessId}: {process.ProcessName}, " +
                                    $"CPU: {process.CpuUsage:F1}%, 内存: {process.MemoryUsageMB}MB, " +
                                    $"窗口数: {process.WindowCount}";
                    await _logService.AddLogEntryAsync(processInfo);
                }
                
                StatusMessage = $"测试完成 - 进程:{edgeProcesses.Length}, CPU:{totalCpu:F1}%, 内存:{totalMemory}MB";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Edge检测测试失败: {ex.Message}");
                await _logService.AddLogEntryAsync($"测试失败: {ex.Message}");
                StatusMessage = "测试失败";
            }
        }

        private async void ExecuteForceKillEdge()
        {
            _logger.LogInformation("手动强制终止目标进程");
            
            var result = _dialogService.ShowConfirmation("强制终止", "确定要强制终止所有目标进程吗？");
            if (!result) return;
            
            try
            {
                StatusMessage = "正在强制终止进程...";
                await _edgeMonitorService.KillAllEdgeProcessesAsync();
                
                var message = "手动强制终止进程完成";
                await _logService.AddMonitorEntryAsync(message);
                await _logService.AddLogEntryAsync(message);
                StatusMessage = "进程已被手动终止";
                
                _dialogService.ShowMessage("操作完成", "所有目标进程已被强制终止");
            }
            catch (Exception ex)
            {
                _logger.LogError($"强制终止进程失败: {ex.Message}");
                await _logService.AddLogEntryAsync($"强制终止失败: {ex.Message}");
                StatusMessage = "强制终止失败";
                _dialogService.ShowMessage("操作失败", $"强制终止进程失败: {ex.Message}");
            }
        }

        private async void ExecuteViewLogStats()
        {
            try
            {
                var stats = await _logService.GetLogStatisticsAsync();
                var message = $"日志统计信息:\n" +
                             $"日志文件数量: {stats.TotalFiles}\n" +
                             $"总文件大小: {stats.TotalSizeMB:F2} MB\n" +
                             $"内存中日志条数: {stats.MemoryLogCount}\n" +
                             $"内存中监控数据条数: {stats.MemoryMonitorCount}\n" +
                             $"日志目录: {stats.LogDirectory}";
                
                _dialogService.ShowMessage("日志统计", message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"获取日志统计失败: {ex.Message}");
                _dialogService.ShowMessage("错误", $"获取日志统计失败: {ex.Message}");
            }
        }

        private void ExecuteOpenLogFolder()
        {
            try
            {
                var logPath = _logService.GetLogFilePath();
                System.Diagnostics.Process.Start("explorer.exe", logPath);
                _logger.LogInformation($"打开日志文件夹: {logPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"打开日志文件夹失败: {ex.Message}");
                _dialogService.ShowMessage("错误", $"打开日志文件夹失败: {ex.Message}");
            }
        }

        // Milestone1: 规则管理
        private async void ExecuteAddRule()
        {
            try
            {
                // 先弹出可视化选择器，支持从应用/进程/窗口选择
                var picker = new PreMonitor.Views.RulePickerWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                bool? picked = picker.ShowDialog();
                var batch = new List<(string Name, string Exec)>();
                if (picked == true && picker.SelectedItems.Count > 0)
                {
                    batch.AddRange(picker.SelectedItems);
                }
                else
                {
                    // 回退到输入对话框，允许手动输入/筛选
                    var dlg = new PreMonitor.Services.InputDialog("添加进程规则", "请输入进程名（不含 .exe）：", "");
                    dlg.Owner = System.Windows.Application.Current.MainWindow;
                    if (dlg.ShowDialog() == true)
                    {
                        var name = dlg.InputText?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(name))
                            batch.Add((name, string.Empty));
                    }
                }

                if (batch.Count == 0) return;

                // 去重：过滤已存在规则的进程名
                var toAdd = batch
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Where(x => !ApplicationRules.Any(r => r.ProcessName.Equals(x.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var (name, exec) in toAdd)
                {
                    var rule = new ApplicationRule
                    {
                        DisplayName = name,
                        ProcessName = name,
                        ExecutablePath = exec,
                        UseGlobalThresholds = true,
                        Enabled = true
                    };
                    ApplicationRules.Add(rule);
                }

                if (toAdd.Count > 0)
                    await ExecuteSaveSettingsInternalAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加规则失败");
            }
        }

        private async void ExecuteRemoveSelectedRule()
        {
            if (SelectedRule == null) return;
            try
            {
                ApplicationRules.Remove(SelectedRule);
                SelectedRule = null;
                await ExecuteSaveSettingsInternalAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除规则失败");
            }
        }

        private async void ExecuteSaveSettings()
        {
            try
            {
                await ExecuteSaveSettingsInternalAsync();
                StatusMessage = "设置已保存";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设置失败");
            }
        }

        private async Task ExecuteSaveSettingsInternalAsync()
        {
            _settings.Rules = ApplicationRules.ToList();
            await _configService.SavePreMonitorSettingsAsync(_settings);
        }

        private async void ExecuteResetCloseChoice()
        {
            try
            {
                _configService.SetValue("UI:RememberCloseChoice", false);
                _configService.SetValue("UI:CloseToTray", "");
                await _configService.SaveAsync();
                
                CloseAction = CloseAction.Ask;
                
                _logger.LogInformation("关闭选择已重置");
                _dialogService.ShowMessage("成功", "关闭选择已重置，下次关闭时将重新询问。");
            }
            catch (Exception ex)
            {
                _logger.LogError($"重置关闭选择失败: {ex.Message}");
                _dialogService.ShowMessage("错误", $"重置关闭选择失败: {ex.Message}");
            }
        }

        private void LoadCloseActionSettings()
        {
            try
            {
                var rememberChoice = _configService.GetValue<bool>("UI:RememberCloseChoice");
                var savedChoice = _configService.GetValue<string>("UI:CloseToTray");

                if (rememberChoice && !string.IsNullOrEmpty(savedChoice))
                {
                    CloseAction = savedChoice.ToLower() switch
                    {
                        "true" => CloseAction.MinimizeToTray,
                        "false" => CloseAction.Exit,
                        _ => CloseAction.Ask
                    };
                }
                else
                {
                    CloseAction = CloseAction.Ask;
                }

                _logger.LogInformation($"加载关闭行为设置: {CloseAction}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"加载关闭行为设置失败: {ex.Message}");
                CloseAction = CloseAction.Ask;
            }
        }

        private async Task SaveCloseActionAsync(CloseAction action)
        {
            try
            {
                _logger.LogInformation($"保存关闭行为设置: {action}");
                
                if (action == CloseAction.Ask)
                {
                    // 如果选择"每次询问"，清除记住的选择
                    _configService.SetValue("UI:RememberCloseChoice", false);
                    _configService.SetValue("UI:CloseToTray", "");
                }
                else
                {
                    // 保存具体的选择
                    var closeToTray = action == CloseAction.MinimizeToTray ? "true" : "false";
                    _configService.SetValue("UI:CloseToTray", closeToTray);
                    _configService.SetValue("UI:RememberCloseChoice", true);
                }
                
                await _configService.SaveAsync();
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"已保存关闭行为设置: {GetCloseActionDisplayName(action)}";
                });
                
                _logger.LogInformation($"关闭行为设置已保存: {action}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存关闭行为设置时发生错误");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"保存关闭行为设置失败: {ex.Message}";
                });
            }
        }

        private static string GetCloseActionDisplayName(CloseAction action)
        {
            return action switch
            {
                CloseAction.Exit => "关闭程序",
                CloseAction.MinimizeToTray => "最小化到托盘",
                CloseAction.Ask => "每次询问",
                _ => "未知"
            };
        }

    private async Task PerformRulesMonitoringAsync()
        {
            // 设置监控标志，防止重叠
            if (_isCurrentlyMonitoring)
            {
                _logger.LogWarning("监控检查已在进行中，忽略重复调用");
                return;
            }

            _isCurrentlyMonitoring = true;
            var startTime = DateTime.Now;
            
            try
            {
                _logger.LogInformation($"[{startTime:HH:mm:ss.fff}] 开始执行规则监控检查");

                var anyProcess = false;
                var anyKilled = false;
                var summaries = new List<string>();

                foreach (var rule in ApplicationRules.Where(r => r.Enabled))
                {
                    var procs = await _processMonitorService.GetProcessesAsync(rule);
                    if (procs == null || procs.Count == 0)
                    {
                        summaries.Add($"{rule.ProcessName}: 无进程");
                        // 无进程则清除超限计时
                        _exceedSince.Remove(rule.ProcessName);
                        continue;
                    }
                    anyProcess = true;

                    var totalCpu = procs.Sum(p => p.CpuUsage);
                    var totalMem = procs.Sum(p => p.MemoryUsageMB);
                    var totalDisk = procs.Sum(p => p.DiskBytesPerSec);
                    var totalGpu = procs.Sum(p => p.GpuUtilPct);
                    var totalNet = procs.Sum(p => p.NetBytesPerSec);
                    var th = rule.UseGlobalThresholds ? GlobalThresholds : (rule.Thresholds ?? GlobalThresholds);

                    var cpuExceeded = th.CpuPct > 0 && totalCpu > th.CpuPct;
                    var memExceeded = th.MemoryMB > 0 && totalMem > th.MemoryMB;
                    var diskExceeded = th.DiskBytesPerSec > 0 && totalDisk > th.DiskBytesPerSec;
                    var gpuExceeded = th.GpuPct > 0 && totalGpu > th.GpuPct;
                    var netExceeded = th.NetBytesPerSec > 0 && totalNet > th.NetBytesPerSec;
                    var exceeded = cpuExceeded || memExceeded || diskExceeded || gpuExceeded || netExceeded;

                    // 计算持续超限时长
                    double sustained = 0;
                    if (exceeded)
                    {
                        if (!_exceedSince.TryGetValue(rule.ProcessName, out var since))
                        {
                            since = DateTime.UtcNow;
                            _exceedSince[rule.ProcessName] = since;
                        }
                        sustained = (DateTime.UtcNow - since).TotalSeconds;
                    }
                    else
                    {
                        _exceedSince.Remove(rule.ProcessName);
                    }

                    var sustainNeed = Math.Max(0, th.SustainSeconds);
                    var sustainText = exceeded ? $", 持续 {Math.Floor(sustained)}/{sustainNeed}s" : string.Empty;
                    summaries.Add($"{rule.ProcessName}: 进程{procs.Count} 个, CPU {totalCpu:F1}%/{th.CpuPct:F1}%, 内存 {totalMem}MB/{th.MemoryMB}MB, 磁盘 {totalDisk}B/s/{th.DiskBytesPerSec}B/s, GPU {totalGpu:F1}%/{th.GpuPct:F1}%, 网络 {totalNet}B/s/{th.NetBytesPerSec}B/s{sustainText}");

                    if (exceeded && (sustainNeed == 0 || sustained >= sustainNeed))
                    {
                        _logger.LogWarning($"[{rule.ProcessName}] 超出阈值: CPU={totalCpu:F1}%>{th.CpuPct:F1}% 或 内存={totalMem}MB>{th.MemoryMB}MB 或 磁盘={totalDisk}B/s>{th.DiskBytesPerSec}B/s 或 GPU={totalGpu:F1}%>{th.GpuPct:F1}% 或 网络={totalNet}B/s>{th.NetBytesPerSec}B/s, 执行终止");
                        await _logService.AddMonitorEntryAsync($"{rule.ProcessName} 超出阈值，正在终止...");
                        foreach (var p in procs)
                        {
                            await _processMonitorService.KillProcessAsync(p.ProcessId);
                        }
                        _exceedSince.Remove(rule.ProcessName);
                        anyKilled = true;
                    }
                }

                var statusInfo = summaries.Count > 0 ? string.Join(" | ", summaries) : "无启用规则";
                System.Windows.Application.Current.Dispatcher.Invoke(() => { StatusMessage = statusInfo; });
                await _logService.AddMonitorEntryAsync(statusInfo);

                if (!anyProcess)
                {
                    _logger.LogInformation("未检测到匹配的规则进程");
                }

                if (anyKilled)
                {
                    await _logService.AddLogEntryAsync("INFO: 已根据规则终止进程");
                }

                var endTime = DateTime.Now;
                _logger.LogInformation($"[{endTime:HH:mm:ss.fff}] 规则监控检查完成，总耗时: {(endTime - startTime).TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                var endTime = DateTime.Now;
                _logger.LogError($"[{endTime:HH:mm:ss.fff}] 规则监控过程中发生错误(耗时: {(endTime - startTime).TotalMilliseconds}ms): {ex.Message}");
                _logger.LogError($"错误堆栈: {ex.StackTrace}");
                
                var errorMessage = $"监控错误: {ex.Message}";
                await _logService.AddLogEntryAsync(errorMessage);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "监控出现错误";
                });
            }
            finally
            {
                // 重置监控标志
                _isCurrentlyMonitoring = false;
            }
        }

        private void LoadStartupSettings()
        {
            try
            {
                IsStartupEnabled = _startupService.IsStartupEnabled();
                IsTrayMonitorStartupEnabled = _startupService.IsTrayMonitorStartupEnabled();
                _logger.LogInformation($"当前开机自启动状态: {IsStartupEnabled}");
                _logger.LogInformation($"当前托盘监测开机自启动状态: {IsTrayMonitorStartupEnabled}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载开机自启动设置时发生错误");
                IsStartupEnabled = false;
                IsTrayMonitorStartupEnabled = false;
            }
        }

        private async Task UpdateStartupStatusAsync(bool enable)
        {
            try
            {
                _logger.LogInformation($"更新开机自启动状态: {enable}");
                
                bool success;
                if (enable)
                {
                    // 启用开机自启动（以管理员权限）
                    success = await _startupService.EnableStartupAsync(true);
                    if (success)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "已启用开机自启动";
                        });
                        _logger.LogInformation("开机自启动已启用（管理员权限）");
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "启用开机自启动失败";
                            // 如果启用失败，需要重置UI状态
                            _isStartupEnabled = false;
                            OnPropertyChanged(nameof(IsStartupEnabled));
                        });
                        _logger.LogError("启用开机自启动失败");
                    }
                }
                else
                {
                    // 禁用开机自启动
                    success = await _startupService.DisableStartupAsync();
                    if (success)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "已禁用开机自启动";
                        });
                        _logger.LogInformation("开机自启动已禁用");
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "禁用开机自启动失败";
                            // 如果禁用失败，需要重置UI状态
                            _isStartupEnabled = true;
                            OnPropertyChanged(nameof(IsStartupEnabled));
                        });
                        _logger.LogError("禁用开机自启动失败");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新开机自启动状态时发生错误");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"开机自启动操作失败: {ex.Message}";
                    // 发生异常时，重置为原来的状态
                    _isStartupEnabled = !enable;
                    OnPropertyChanged(nameof(IsStartupEnabled));
                });
            }
        }

        private async Task UpdateTrayMonitorStartupStatusAsync(bool enable)
        {
            try
            {
                _logger.LogInformation($"更新托盘监测开机自启动状态: {enable}");
                
                bool success;
                if (enable)
                {
                    // 启用托盘监测开机自启动
                    success = await _startupService.EnableTrayMonitorStartupAsync();
                    if (success)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "已启用开机后自动在托盘监测";
                        });
                        _logger.LogInformation("托盘监测开机自启动已启用");
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "启用托盘监测开机自启动失败";
                            // 如果启用失败，需要重置UI状态
                            _isTrayMonitorStartupEnabled = false;
                            OnPropertyChanged(nameof(IsTrayMonitorStartupEnabled));
                        });
                        _logger.LogError("启用托盘监测开机自启动失败");
                    }
                }
                else
                {
                    // 禁用托盘监测开机自启动
                    success = await _startupService.DisableStartupAsync();
                    if (success)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "已禁用托盘监测开机自启动";
                        });
                        _logger.LogInformation("托盘监测开机自启动已禁用");
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "禁用托盘监测开机自启动失败";
                            // 如果禁用失败，需要重置UI状态
                            _isTrayMonitorStartupEnabled = true;
                            OnPropertyChanged(nameof(IsTrayMonitorStartupEnabled));
                        });
                        _logger.LogError("禁用托盘监测开机自启动失败");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新托盘监测开机自启动状态时发生错误");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"托盘监测开机自启动操作失败: {ex.Message}";
                    // 发生异常时，重置为原来的状态
                    _isTrayMonitorStartupEnabled = !enable;
                    OnPropertyChanged(nameof(IsTrayMonitorStartupEnabled));
                });
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
