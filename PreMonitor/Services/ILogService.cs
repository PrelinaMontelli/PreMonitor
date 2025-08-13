using System.Collections.ObjectModel;

namespace PreMonitor.Services
{
    public interface ILogService
    {
        /// <summary>
        /// 获取内存中的日志条目（最新300条）
        /// </summary>
        ObservableCollection<string> LogEntries { get; }

        /// <summary>
        /// 获取内存中的监控数据（最新300条）
        /// </summary>
        ObservableCollection<string> MonitorEntries { get; }

        /// <summary>
        /// 添加日志条目
        /// </summary>
        Task AddLogEntryAsync(string message);

        /// <summary>
        /// 添加监控数据条目
        /// </summary>
        Task AddMonitorEntryAsync(string message);

        /// <summary>
        /// 清除内存中的日志（文件中的不删除）
        /// </summary>
        void ClearMemoryLogs();

        /// <summary>
        /// 获取日志文件路径
        /// </summary>
        string GetLogFilePath();

        /// <summary>
        /// 清理超过指定天数的日志文件
        /// </summary>
        Task CleanupOldLogFilesAsync();

        /// <summary>
        /// 获取日志统计信息
        /// </summary>
        Task<LogStatistics> GetLogStatisticsAsync();
    }

    public class LogStatistics
    {
        public int TotalFiles { get; set; }
        public long TotalSizeBytes { get; set; }
        public double TotalSizeMB { get; set; }
        public int MemoryLogCount { get; set; }
        public int MemoryMonitorCount { get; set; }
        public string LogDirectory { get; set; } = "";
    }
}
