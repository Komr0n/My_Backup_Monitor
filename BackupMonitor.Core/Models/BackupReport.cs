using System;
using System.Collections.Generic;

namespace BackupMonitor.Core.Models
{
    public class BackupReport
    {
        public DateTime GeneratedAt { get; set; }
        public List<ServiceReport> Services { get; set; } = new List<ServiceReport>();

        public class ServiceReport
        {
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public List<DateTime> MissingDates { get; set; } = new List<DateTime>();
        }
    }
}
