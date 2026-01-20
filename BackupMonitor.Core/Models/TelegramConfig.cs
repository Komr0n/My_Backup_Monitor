using System.Collections.Generic;

namespace BackupMonitor.Core.Models
{
    public class TelegramConfig
    {
        public bool Enabled { get; set; } = false;
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public ReportMode ReportMode { get; set; } = ReportMode.FailOnly;
        public List<string> SendTimes { get; set; } = new List<string>();
    }

    public enum ReportMode
    {
        FailOnly,      // Только FAIL
        OkOnly,        // Только OK
        Full           // Полный отчёт (OK + FAIL)
    }
}
