using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace PreMonitor.Services
{
    public interface IEdgeMonitorService
    {
        Task<EdgeProcessInfo[]> GetEdgeProcessesAsync();
        Task<bool> HasVisibleWindowsAsync();
        Task KillAllEdgeProcessesAsync();
        bool IsEdgeRunningInBackground(EdgeProcessInfo[] processes);
        bool HasAbnormalResourceUsage(EdgeProcessInfo[] processes, double cpuThreshold = 30.0, long memoryThreshold = 2048);
    }

    public class EdgeProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public double CpuUsage { get; set; }
        public long MemoryUsageMB { get; set; }
        public int WindowCount { get; set; }
        public string CommandLine { get; set; } = "";
        public DateTime StartTime { get; set; }
    }

    public class EdgeMonitorService : IEdgeMonitorService
    {
        private readonly ILogger<EdgeMonitorService> _logger;
        private readonly Dictionary<int, double> _previousCpuTimes = new();
        private readonly Dictionary<int, DateTime> _previousSampleTimes = new();

        // Windows API 声明
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public EdgeMonitorService(ILogger<EdgeMonitorService> logger)
        {
            _logger = logger;
        }

        public async Task<EdgeProcessInfo[]> GetEdgeProcessesAsync()
        {
            return await Task.Run(() =>
            {
                var startTime = DateTime.Now;
                var edgeProcesses = new List<EdgeProcessInfo>();
                
                // 修改为只搜索现代Edge进程以提高性能
                _logger.LogInformation("开始搜索Edge进程");
                
                var processes = Process.GetProcessesByName("msedge");
                _logger.LogInformation($"找到 {processes.Length} 个 msedge 进程");
                
                // 获取所有Edge进程的窗口信息（一次性处理，避免重复枚举）
                var processWindowCounts = GetAllProcessWindowCounts(processes.Select(p => p.Id).ToArray());
                
                // 并行处理进程信息以提高性能
                var processInfos = processes.AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount)
                    .Select(process =>
                    {
                        try
                        {
                            var processInfo = new EdgeProcessInfo
                            {
                                ProcessId = process.Id,
                                ProcessName = process.ProcessName,
                                MemoryUsageMB = process.WorkingSet64 / (1024 * 1024),
                                StartTime = process.StartTime,
                                WindowCount = processWindowCounts.GetValueOrDefault(process.Id, 0),
                                CommandLine = "已省略" // 移除耗时的命令行获取
                            };

                            // 计算CPU使用率
                            processInfo.CpuUsage = CalculateCpuUsage(process);

                            _logger.LogDebug($"处理进程: PID={processInfo.ProcessId}, 内存={processInfo.MemoryUsageMB}MB, 窗口数={processInfo.WindowCount}");
                            return processInfo;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"无法获取进程 {process.ProcessName} (PID: {process.Id}) 的信息: {ex.Message}");
                            return null;
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    })
                    .Where(info => info != null)
                    .ToList();

                edgeProcesses.AddRange(processInfos!);
                
                var endTime = DateTime.Now;
                var elapsed = (endTime - startTime).TotalMilliseconds;
                _logger.LogInformation($"Edge进程扫描完成，耗时: {elapsed:F0}ms，找到 {edgeProcesses.Count} 个进程");
                
                return edgeProcesses.ToArray();
            });
        }

        public async Task<bool> HasVisibleWindowsAsync()
        {
            return await Task.Run(() =>
            {
                var edgeProcesses = Process.GetProcessesByName("msedge");
                var hasVisibleWindows = false;

                var processIds = edgeProcesses.Select(p => (uint)p.Id).ToHashSet();

                EnumWindows((hWnd, lParam) =>
                {
                    if (IsWindowVisible(hWnd))
                    {
                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (processIds.Contains(processId))
                        {
                            // 检查窗口是否有标题（通常表示是真正的用户界面窗口）
                            int length = GetWindowTextLength(hWnd);
                            if (length > 0)
                            {
                                var title = new System.Text.StringBuilder(length + 1);
                                GetWindowText(hWnd, title, title.Capacity);
                                
                                // 过滤掉一些系统窗口或隐藏窗口
                                var titleText = title.ToString();
                                if (!string.IsNullOrWhiteSpace(titleText) && 
                                    !titleText.Contains("Microsoft Edge WebView2") &&
                                    !titleText.Contains("DevTools"))
                                {
                                    hasVisibleWindows = true;
                                    return false; // 停止枚举
                                }
                            }
                        }
                    }
                    return true; // 继续枚举
                }, IntPtr.Zero);

                foreach (var process in edgeProcesses)
                {
                    process.Dispose();
                }

                return hasVisibleWindows;
            });
        }

        public async Task KillAllEdgeProcessesAsync()
        {
            await Task.Run(() =>
            {
                // 只终止现代Edge进程，提高性能
                var processes = Process.GetProcessesByName("msedge");
                int killedCount = 0;

                foreach (var process in processes)
                {
                    try
                    {
                        _logger.LogInformation($"正在终止 Edge 进程: {process.ProcessName} (PID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(3000); // 减少等待时间到3秒
                        killedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"无法终止进程 {process.ProcessName} (PID: {process.Id}): {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                _logger.LogInformation($"已终止 {killedCount} 个 Edge 进程");
            });
        }

        public bool IsEdgeRunningInBackground(EdgeProcessInfo[] processes)
        {
            return processes.Any() && processes.All(p => p.WindowCount == 0);
        }

        public bool HasAbnormalResourceUsage(EdgeProcessInfo[] processes, double cpuThreshold = 30.0, long memoryThreshold = 2048)
        {
            if (!processes.Any()) return false;

            var totalCpu = processes.Sum(p => p.CpuUsage);
            var totalMemory = processes.Sum(p => p.MemoryUsageMB);

            return totalCpu > cpuThreshold || totalMemory > memoryThreshold;
        }

        private Dictionary<int, int> GetAllProcessWindowCounts(int[] processIds)
        {
            var windowCounts = new Dictionary<int, int>();
            var processIdSet = processIds.ToHashSet();
            
            // 初始化所有进程的窗口计数为0
            foreach (var pid in processIds)
            {
                windowCounts[pid] = 0;
            }

            // 一次性枚举所有窗口，避免重复枚举
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    GetWindowThreadProcessId(hWnd, out uint processId);
                    if (processIdSet.Contains((int)processId))
                    {
                        int length = GetWindowTextLength(hWnd);
                        if (length > 0)
                        {
                            windowCounts[(int)processId]++;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return windowCounts;
        }

        private int GetWindowCountForProcess(int processId)
        {
            int windowCount = 0;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint winProcessId);
                if (winProcessId == processId && IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        windowCount++;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return windowCount;
        }

        private double CalculateCpuUsage(Process process)
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var currentCpuTime = process.TotalProcessorTime.TotalMilliseconds;

                if (_previousCpuTimes.ContainsKey(process.Id) && _previousSampleTimes.ContainsKey(process.Id))
                {
                    var previousCpuTime = _previousCpuTimes[process.Id];
                    var previousSampleTime = _previousSampleTimes[process.Id];

                    var cpuTimeDelta = currentCpuTime - previousCpuTime;
                    var realTimeDelta = (currentTime - previousSampleTime).TotalMilliseconds;

                    if (realTimeDelta > 0)
                    {
                        var cpuUsage = (cpuTimeDelta / realTimeDelta) * 100.0 / Environment.ProcessorCount;
                        
                        _previousCpuTimes[process.Id] = currentCpuTime;
                        _previousSampleTimes[process.Id] = currentTime;
                        
                        return Math.Max(0, Math.Min(100, cpuUsage));
                    }
                }

                // 第一次检测时，记录初始值
                _previousCpuTimes[process.Id] = currentCpuTime;
                _previousSampleTimes[process.Id] = currentTime;
                
                // 快速估算：第一次检测时返回0，避免耗时的进程年龄计算
                return 0.0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"计算进程 {process.Id} CPU使用率失败: {ex.Message}");
                return 0.0;
            }
        }
    }
}
