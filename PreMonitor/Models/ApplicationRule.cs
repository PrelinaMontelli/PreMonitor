using System;

namespace PreMonitor.Models
{
    public class ApplicationRule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string DisplayName { get; set; } = "";        // 显示名称
        public string ProcessName { get; set; } = "";        // 进程名（不含扩展名）
        public string ExecutablePath { get; set; } = "";     // 可执行路径（可选）
        public bool Enabled { get; set; } = true;             // 是否启用监控
        public bool UseGlobalThresholds { get; set; } = true; // 是否使用全局阈值
        public Thresholds? Thresholds { get; set; }           // 单独阈值（当 UseGlobalThresholds=false 时生效）
            = new Thresholds();
    }
}
