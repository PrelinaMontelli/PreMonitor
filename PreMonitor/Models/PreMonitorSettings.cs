using System.Collections.Generic;

namespace PreMonitor.Models
{
    public class PreMonitorSettings
    {
        public int Version { get; set; } = 1;
        public Thresholds GlobalThresholds { get; set; } = new Thresholds();
        public List<ApplicationRule> Rules { get; set; } = new List<ApplicationRule>();
    }
}
