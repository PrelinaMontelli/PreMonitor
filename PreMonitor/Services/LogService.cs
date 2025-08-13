using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace PreMonitor.Services
{
    public class LogService : ILogService
    {
        private readonly ILogger<LogService> _logger;
        private readonly IConfiguration _configuration;
        private const int MAX_MEMORY_ENTRIES = 300;
        private readonly string _logDirectory;
        private readonly string _logFilePath;
        private readonly string _monitorFilePath;
        private readonly object _logLock = new object();
        private readonly object _monitorLock = new object();

        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> MonitorEntries { get; } = new ObservableCollection<string>();

        public LogService(ILogger<LogService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // 创建日志目录
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PreMonitor", "Logs");
            Directory.CreateDirectory(_logDirectory);

            // 设置日志文件路径
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(_logDirectory, $"PreMonitor-{today}.log");
            _monitorFilePath = Path.Combine(_logDirectory, $"PreMonitor-Monitor-{today}.log");

            _logger.LogInformation($"日志服务已初始化，日志目录: {_logDirectory}");
        }

        public async Task AddLogEntryAsync(string message)
        {
            var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            
            // 异步写入文件
            await WriteToFileAsync(_logFilePath, timestampedMessage);

            // 在UI线程上更新内存集合
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_logLock)
                {
                    LogEntries.Add(timestampedMessage);
                    
                    // 保持内存中只有最新的300条记录
                    while (LogEntries.Count > MAX_MEMORY_ENTRIES)
                    {
                        LogEntries.RemoveAt(0);
                    }
                }
            });
        }

        public async Task AddMonitorEntryAsync(string message)
        {
            var timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            
            // 异步写入文件
            await WriteToFileAsync(_monitorFilePath, timestampedMessage);

            // 在UI线程上更新内存集合
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_monitorLock)
                {
                    MonitorEntries.Add(timestampedMessage);
                    
                    // 保持内存中只有最新的300条记录
                    while (MonitorEntries.Count > MAX_MEMORY_ENTRIES)
                    {
                        MonitorEntries.RemoveAt(0);
                    }
                }
            });
        }

        public void ClearMemoryLogs()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_logLock)
                {
                    LogEntries.Clear();
                }
                lock (_monitorLock)
                {
                    MonitorEntries.Clear();
                }
            });

            _logger.LogInformation("内存日志已清除（文件日志保留）");
        }

        public string GetLogFilePath()
        {
            return _logDirectory;
        }

        private async Task WriteToFileAsync(string filePath, string message)
        {
            try
            {
                // 确保文件所在目录存在
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 异步追加写入文件
                using (var writer = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8))
                {
                    await writer.WriteLineAsync(message);
                    await writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"写入日志文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理超过指定天数的日志文件
        /// </summary>
        public async Task CleanupOldLogFilesAsync()
        {
            try
            {
                var retentionDays = _configuration.GetValue<int>("ApplicationSettings:LogRetentionDays", 30);
                var cutoffDate = DateTime.Now.AddDays(-retentionDays);

                await Task.Run(() =>
                {
                    var logFiles = Directory.GetFiles(_logDirectory, "*.log");
                    var deletedCount = 0;

                    foreach (var file in logFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < cutoffDate)
                        {
                            try
                            {
                                File.Delete(file);
                                deletedCount++;
                                _logger.LogInformation($"已删除过期日志文件: {Path.GetFileName(file)}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"删除日志文件失败 {Path.GetFileName(file)}: {ex.Message}");
                            }
                        }
                    }

                    if (deletedCount > 0)
                    {
                        _logger.LogInformation($"日志清理完成，共删除 {deletedCount} 个过期文件");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"清理日志文件时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取日志统计信息
        /// </summary>
        public async Task<LogStatistics> GetLogStatisticsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var logFiles = Directory.GetFiles(_logDirectory, "*.log");
                    var totalSize = logFiles.Sum(f => new FileInfo(f).Length);
                    var totalFiles = logFiles.Length;

                    return new LogStatistics
                    {
                        TotalFiles = totalFiles,
                        TotalSizeBytes = totalSize,
                        TotalSizeMB = totalSize / (1024.0 * 1024.0),
                        MemoryLogCount = LogEntries.Count,
                        MemoryMonitorCount = MonitorEntries.Count,
                        LogDirectory = _logDirectory
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError($"获取日志统计信息失败: {ex.Message}");
                    return new LogStatistics
                    {
                        LogDirectory = _logDirectory,
                        MemoryLogCount = LogEntries.Count,
                        MemoryMonitorCount = MonitorEntries.Count
                    };
                }
            });
        }
    }
}
