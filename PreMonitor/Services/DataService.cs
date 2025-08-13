namespace PreMonitor.Services
{
    public interface IDataService
    {
        Task<string> LoadDataAsync();
        Task SaveDataAsync(string data);
        Task<List<string>> GetLogEntriesAsync();
        Task AddLogEntryAsync(string entry);
    }

    public class DataService : IDataService
    {
        private readonly List<string> _logEntries = new();

        public async Task<string> LoadDataAsync()
        {
            // 模拟异步数据加载
            await Task.Delay(100);
            return "示例数据已加载";
        }

        public async Task SaveDataAsync(string data)
        {
            // 模拟异步数据保存
            await Task.Delay(100);
            await AddLogEntryAsync($"数据已保存: {data.Length} 字符");
        }

        public async Task<List<string>> GetLogEntriesAsync()
        {
            await Task.Delay(50);
            return new List<string>(_logEntries);
        }

        public async Task AddLogEntryAsync(string entry)
        {
            await Task.Delay(10);
            _logEntries.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {entry}");
        }
    }
}
