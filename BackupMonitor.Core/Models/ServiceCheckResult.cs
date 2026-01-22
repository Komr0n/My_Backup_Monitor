using System;
using System.Collections.Generic;

namespace BackupMonitor.Core.Models
{
    public class ServiceCheckResult
    {
        public string ServiceName { get; set; } = string.Empty;
        public ServiceCheckStatus Status { get; set; } = ServiceCheckStatus.FAIL;
        public DateTime ExpectedDate { get; set; }
        public int FoundCount { get; set; }
        public int MinRequiredCount { get; set; }
        public DateTime? LastObservedBackupDate { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; set; } = new List<string>();
        public List<ServiceCheckResult> Children { get; set; } = new List<ServiceCheckResult>();
    }
}
