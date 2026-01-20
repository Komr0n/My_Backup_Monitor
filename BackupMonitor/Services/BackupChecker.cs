// Этот файл оставлен для обратной совместимости
// Все методы делегируются в BackupMonitor.Core.Services.BackupChecker
using BackupMonitor.Core.Services;
using BackupMonitor.Core.Models;
using CheckResult = BackupMonitor.Core.Services.BackupChecker.CheckResult;

namespace BackupMonitor.Services
{
    public class BackupChecker
    {
        private readonly BackupMonitor.Core.Services.BackupChecker _coreChecker;

        public BackupChecker()
        {
            _coreChecker = new BackupMonitor.Core.Services.BackupChecker();
        }

        public CheckResult CheckBackupForDate(Service service, System.DateTime targetDate)
        {
            return _coreChecker.CheckBackupForDate(service, targetDate);
        }

        public CheckResult CheckBackupForPeriod(Service service, System.DateTime startDate, System.DateTime endDate)
        {
            return _coreChecker.CheckBackupForPeriod(service, startDate, endDate);
        }
    }
}
