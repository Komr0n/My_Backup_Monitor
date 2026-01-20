using System.Collections.Generic;

namespace BackupMonitor.Core.Models
{
    public class Service
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new List<string>();
        public List<string> DatePatterns { get; set; } = new List<string>();
    }
}
