using System;
using System.Collections.Generic;

namespace BackupMonitor.Core.Models
{
    public class BackupReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<ServiceCheckResult> Services { get; set; } = new List<ServiceCheckResult>();
    }
}
