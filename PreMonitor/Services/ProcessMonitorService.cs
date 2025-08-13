using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PreMonitor.Models;

namespace PreMonitor.Services
{
    public interface IProcessMonitorService
    {
        Task<IReadOnlyList<MonitoredProcessInfo>> GetProcessesAsync(ApplicationRule rule);
        Task KillProcessAsync(int pid);
    }

    public class MonitoredProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public double CpuUsage { get; set; } // %
        public long MemoryUsageMB { get; set; }
    public long DiskBytesPerSec { get; set; } // 实际为进程总 I/O 读写速率（包含磁盘/网络/管道），作为“磁盘”阈值的近似
    public double GpuUtilPct { get; set; } // GPU 利用率（总和，近似）
    public long NetBytesPerSec { get; set; } // 近似网络速率：IO Other Bytes/sec
    }

    public class ProcessMonitorService : IProcessMonitorService
    {
        private readonly ILogger<ProcessMonitorService> _logger;
    private readonly Dictionary<int, double> _prevCpuTimes = new();
    private readonly Dictionary<int, DateTime> _prevSampleTimes = new();
    // 进程性能计数器缓存（按 PID）
    private readonly Dictionary<int, (PerformanceCounter read, PerformanceCounter write)> _ioCounters = new();
    private readonly Dictionary<int, PerformanceCounter> _ioOtherCounters = new();
    private readonly Dictionary<int, List<PerformanceCounter>> _gpuCounters = new();
    private readonly object _counterLock = new();

        public ProcessMonitorService(ILogger<ProcessMonitorService> logger)
        {
            _logger = logger;
        }

        public async Task<IReadOnlyList<MonitoredProcessInfo>> GetProcessesAsync(ApplicationRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.ProcessName)) return Array.Empty<MonitoredProcessInfo>();

            return await Task.Run(() =>
            {
                var list = new List<MonitoredProcessInfo>();
                Process[] processes = Array.Empty<Process>();
                try
                {
                    processes = Process.GetProcessesByName(rule.ProcessName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取进程列表失败: {name}", rule.ProcessName);
                    return (IReadOnlyList<MonitoredProcessInfo>)list;
                }

                foreach (var p in processes)
                {
                    try
                    {
                        var io = GetProcessIoBytesPerSec(p);
                        var ioOther = GetProcessIoOtherBytesPerSec(p);
                        var gpu = GetProcessGpuUtilPct(p);
                        var info = new MonitoredProcessInfo
                        {
                            ProcessId = p.Id,
                            ProcessName = p.ProcessName,
                            MemoryUsageMB = p.WorkingSet64 / (1024 * 1024),
                            CpuUsage = CalculateCpuUsage(p),
                            DiskBytesPerSec = io,
                            GpuUtilPct = gpu,
                            NetBytesPerSec = ioOther
                        };
                        list.Add(info);
                    }
                    catch
                    {
                        // ignore per-process failure
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
                return (IReadOnlyList<MonitoredProcessInfo>)list;
            });
        }

        public async Task KillProcessAsync(int pid)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "终止进程失败: {pid}", pid);
                }
            });
        }

        private double CalculateCpuUsage(Process process)
        {
            try
            {
                var now = DateTime.UtcNow;
                var currentCpu = process.TotalProcessorTime.TotalMilliseconds;
                if (_prevCpuTimes.TryGetValue(process.Id, out var prevCpu) && _prevSampleTimes.TryGetValue(process.Id, out var prevTs))
                {
                    var cpuDelta = currentCpu - prevCpu;
                    var timeDelta = (now - prevTs).TotalMilliseconds;
                    if (timeDelta > 0)
                    {
                        var usage = (cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount;
                        _prevCpuTimes[process.Id] = currentCpu;
                        _prevSampleTimes[process.Id] = now;
                        return Math.Clamp(usage, 0, 100);
                    }
                }
                _prevCpuTimes[process.Id] = currentCpu;
                _prevSampleTimes[process.Id] = now;
                return 0.0; // first sample
            }
            catch
            {
                return 0.0;
            }
        }

        private long GetProcessIoBytesPerSec(Process process)
        {
            try
            {
                var pid = process.Id;
                (PerformanceCounter read, PerformanceCounter write) counters;
                lock (_counterLock)
                {
                    if (!_ioCounters.TryGetValue(pid, out counters))
                    {
                        var instance = ResolveProcessInstanceName(process);
                        if (instance == null)
                            return 0;
                        var readCounter = new PerformanceCounter("Process", "IO Read Bytes/sec", instance, true);
                        var writeCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", instance, true);
                        // 第一次 NextValue() 通常为 0，但缓存下来供后续调用
                        counters = (readCounter, writeCounter);
                        _ioCounters[pid] = counters;
                    }
                }

                float read = 0, write = 0;
                try { read = counters.read.NextValue(); } catch { }
                try { write = counters.write.NextValue(); } catch { }
                var total = (long)Math.Max(0, read + write);
                return total;
            }
            catch
            {
                return 0;
            }
            finally
            {
                // 清理已退出进程的计数器（轻量策略）：定期尝试移除不存在的 PID
                // 这里不做重型遍历，仅在异常较多时调用外部清理更合适。
            }
        }

        private long GetProcessIoOtherBytesPerSec(Process process)
        {
            try
            {
                var pid = process.Id;
                PerformanceCounter? counter;
                lock (_counterLock)
                {
                    if (!_ioOtherCounters.TryGetValue(pid, out counter) || counter == null)
                    {
                        var instance = ResolveProcessInstanceName(process);
                        if (instance == null)
                            return 0;
                        counter = new PerformanceCounter("Process", "IO Other Bytes/sec", instance, true);
                        _ioOtherCounters[pid] = counter;
                    }
                }
                if (counter == null) return 0;
                float other = 0;
                try { other = counter.NextValue(); } catch { }
                return (long)Math.Max(0, other);
            }
            catch
            {
                return 0;
            }
        }

        private double GetProcessGpuUtilPct(Process process)
        {
            try
            {
                // 使用 "GPU Engine" 计数器：实例名形如 "pid_{pid}_*"，计数器 "Utilization Percentage"
                var pid = process.Id;
                List<PerformanceCounter>? list;
                lock (_counterLock)
                {
                    if (!_gpuCounters.TryGetValue(pid, out list) || list == null)
                    {
                        list = new List<PerformanceCounter>();
                        var cat = new PerformanceCounterCategory("GPU Engine");
                        foreach (var inst in cat.GetInstanceNames())
                        {
                            if (!inst.StartsWith("pid_" + pid.ToString(), StringComparison.OrdinalIgnoreCase))
                                continue;
                            try
                            {
                                // 仅统计 3D/Compute 引擎更合理，这里先汇总所有该 pid 的实例
                                var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                                // 预热一次
                                _ = c.NextValue();
                                list.Add(c);
                            }
                            catch { }
                        }
                        _gpuCounters[pid] = list;
                    }
                }
                if (list == null || list.Count == 0) return 0.0;
                double sum = 0;
                foreach (var c in list)
                {
                    try { sum += c.NextValue(); } catch { }
                }
                // 该计数器已是百分比，多个引擎求和后近似为本进程总 GPU 利用率（可能>100，做裁剪）
                return Math.Clamp(sum, 0, 100);
            }
            catch
            {
                return 0.0;
            }
        }

        private static string? ResolveProcessInstanceName(Process p)
        {
            try
            {
                var category = new PerformanceCounterCategory("Process");
                var instances = category.GetInstanceNames();
                // 进程名可能有 #n 后缀，需通过“ID Process”匹配 PID
                foreach (var name in instances)
                {
                    if (!name.StartsWith(p.ProcessName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        using var idCounter = new PerformanceCounter("Process", "ID Process", name, true);
                        var val = (int)idCounter.NextValue();
                        if (val == p.Id)
                            return name;
                    }
                    catch
                    {
                        // ignore this instance
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
