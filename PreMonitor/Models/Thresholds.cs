namespace PreMonitor.Models
{
    public class Thresholds
    {
        public double CpuPct { get; set; } = 30.0;        // 0-100
        public long MemoryMB { get; set; } = 2048;         // MB
        public long DiskBytesPerSec { get; set; } = 0;     // 0 表示未启用
        public double GpuPct { get; set; } = 0;            // 0-100, 0 表示未启用
        public long NetBytesPerSec { get; set; } = 0;      // 0 表示未启用
    public int SustainSeconds { get; set; } = 5;       // 持续超限秒数后再终止
    }
}
