// Этот файл оставлен для обратной совместимости
// Все методы делегируются в BackupMonitor.Core.Services.TelegramReportSender
using System.Threading.Tasks;
using BackupMonitor.Core.Models;
using BackupMonitor.Core.Services;

namespace BackupMonitor.Services
{
    public class TelegramReportSender
    {
        private readonly BackupMonitor.Core.Services.TelegramReportSender _coreSender;

        public TelegramReportSender()
        {
            _coreSender = new BackupMonitor.Core.Services.TelegramReportSender();
        }

        public Task<bool> SendReportAsync(TelegramConfig config, BackupReport report)
        {
            return _coreSender.SendReportAsync(config, report);
        }

        public void Dispose()
        {
            _coreSender.Dispose();
        }
    }
}
