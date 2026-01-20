using System.Collections.Generic;

namespace BackupMonitor.Core.Models
{
    public class AppConfig
    {
        public List<Service> Services { get; set; } = new List<Service>();
        public TelegramConfig Telegram { get; set; } = new TelegramConfig();
    }
}
