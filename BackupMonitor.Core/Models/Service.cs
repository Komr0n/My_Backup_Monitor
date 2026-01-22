using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BackupMonitor.Core.Models
{
    public class Service
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new List<string>();
        public List<string> DatePatterns { get; set; } = new List<string>();
        public int ExpectedDayOffset { get; set; } = 0;
        [JsonConverter(typeof(StringEnumConverter))]
        public ServiceCheckMode CheckMode { get; set; } = ServiceCheckMode.NameDate;
        [JsonConverter(typeof(StringEnumConverter))]
        public FileTimeSource FileTimeSource { get; set; } = FileTimeSource.LastWriteTime;
        public int MinFilesPerDay { get; set; } = 1;
        public string? FileMask { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ServiceType Type { get; set; } = ServiceType.Single;
        public List<Service> Children { get; set; } = new List<Service>();
        public bool Required { get; set; } = true;
        public List<string> ChildFolders { get; set; } = new List<string>();
        public bool UseChildFolderAsKeyword { get; set; } = true;
    }
}
