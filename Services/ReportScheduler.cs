using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Services
{
    public class ReportScheduler
    {
        private DispatcherTimer _timer;
        private BackupMonitor.Services.ConfigurationManager _configManager;
        private BackupMonitor.Services.BackupChecker _backupChecker;
        private BackupMonitor.Services.TelegramReportSender _telegramSender;
        private HashSet<string> _sentTimesToday = new HashSet<string>();

        public ReportScheduler(BackupMonitor.Services.ConfigurationManager configManager, BackupMonitor.Services.BackupChecker backupChecker, BackupMonitor.Services.TelegramReportSender telegramSender)
        {
            _configManager = configManager;
            _backupChecker = backupChecker;
            _telegramSender = telegramSender;
            
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(30); // Проверяем каждые 30 секунд для более точного срабатывания
            _timer.Tick += Timer_Tick;
        }

        public void Start()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                System.Diagnostics.Debug.WriteLine("Планировщик отчётов запущен");
            }
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var config = _configManager.TelegramConfig;
            if (!config.Enabled || config.SendTimes.Count == 0)
                return;

            var now = DateTime.Now;
            var currentTime = now.ToString("HH:mm");
            var todayKey = now.ToString("yyyy-MM-dd");

            // Сбрасываем список отправленных времён при смене дня
            if (!_sentTimesToday.Contains(todayKey))
            {
                _sentTimesToday.Clear();
                _sentTimesToday.Add(todayKey);
                System.Diagnostics.Debug.WriteLine($"Новый день: {todayKey}, сброс списка отправленных отчётов");
            }

            // Проверяем, нужно ли отправить отчёт сейчас
            foreach (var sendTime in config.SendTimes)
            {
                var timeKey = $"{todayKey}_{sendTime}";
                
                // Если уже отправили в это время сегодня - пропускаем
                if (_sentTimesToday.Contains(timeKey))
                    continue;

                // Проверяем, наступило ли время отправки (с точностью до минуты)
                if (IsTimeToSend(currentTime, sendTime))
                {
                    System.Diagnostics.Debug.WriteLine($"Время отправки наступило: {currentTime} == {sendTime}");
                    _sentTimesToday.Add(timeKey);
                    Task.Run(() => SendScheduledReportAsync(config));
                }
            }
        }

        private bool IsTimeToSend(string currentTime, string scheduledTime)
        {
            // Парсим время в формате HH:mm
            var currentParts = currentTime.Split(':');
            var scheduledParts = scheduledTime.Split(':');
            
            if (currentParts.Length == 2 && scheduledParts.Length == 2)
            {
                if (int.TryParse(currentParts[0], out var currentHour) &&
                    int.TryParse(currentParts[1], out var currentMinute) &&
                    int.TryParse(scheduledParts[0], out var scheduledHour) &&
                    int.TryParse(scheduledParts[1], out var scheduledMinute))
                {
                    bool matches = currentHour == scheduledHour && currentMinute == scheduledMinute;
                    if (matches)
                    {
                        System.Diagnostics.Debug.WriteLine($"Время совпало: текущее {currentTime}, запланированное {scheduledTime}");
                    }
                    return matches;
                }
            }
            return false;
        }

        private async Task SendScheduledReportAsync(BackupMonitor.Core.Models.TelegramConfig config)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Начало отправки запланированного отчёта в {DateTime.Now:HH:mm:ss}");
                var report = GenerateReport();
                var preSendResult = GetPreSendResult(config, report);
                if (preSendResult.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine($"Отчёт не отправлен: {preSendResult.Value.Message}");
                    return;
                }

                var success = await _telegramSender.SendReportAsync(config, report);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Запланированный отчёт успешно отправлен");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Не удалось отправить запланированный отчёт (вернулся false)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки запланированного отчёта: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        public async Task<ReportSendResult> SendReportNowAsync()
        {
            var config = _configManager.TelegramConfig;
            if (!config.Enabled)
            {
                throw new Exception("Telegram уведомления отключены");
            }

            if (string.IsNullOrWhiteSpace(config.BotToken))
            {
                throw new Exception("Bot Token не настроен");
            }

            if (string.IsNullOrWhiteSpace(config.ChatId))
            {
                throw new Exception("Chat ID не настроен");
            }

            var report = GenerateReport();
            var preSendResult = GetPreSendResult(config, report);
            if (preSendResult.HasValue)
            {
                return preSendResult.Value;
            }

            var success = await _telegramSender.SendReportAsync(config, report);
            return success
                ? ReportSendResult.Sent()
                : ReportSendResult.Failed("Не удалось отправить отчет");
        }

        private ReportSendResult? GetPreSendResult(TelegramConfig config, BackupMonitor.Core.Models.BackupReport report)
        {
            if (report.Services.Count == 0)
            {
                return ReportSendResult.Failed("Нет настроенных сервисов для отчета");
            }

            if (config.ReportMode == ReportMode.FailOnly && report.Services.All(s => s.IsValid))
            {
                return ReportSendResult.Skipped("Все сервисы в статусе OK. Режим FAIL_ONLY");
            }

            if (config.ReportMode == ReportMode.OkOnly && report.Services.All(s => !s.IsValid))
            {
                return ReportSendResult.Skipped("Все сервисы в статусе FAIL. Режим OK_ONLY");
            }

            return null;
        }

        private BackupMonitor.Core.Models.BackupReport GenerateReport()
        {
            var report = new BackupMonitor.Core.Models.BackupReport
            {
                GeneratedAt = DateTime.Now
            };

            var today = DateTime.Now.Date;

            foreach (var service in _configManager.Services)
            {
                var result = _backupChecker.CheckBackupForDate(service, today);
                
                var serviceReport = new BackupMonitor.Core.Models.BackupReport.ServiceReport
                {
                    Name = service.Name,
                    Path = service.Path,
                    IsValid = result.IsValid,
                    ErrorMessage = result.ErrorMessage
                };

                if (!result.IsValid && result.MissingDates.Count > 0)
                {
                    serviceReport.MissingDates.AddRange(result.MissingDates);
                }

                report.Services.Add(serviceReport);
            }

            return report;
        }
    }

    public enum ReportSendStatus
    {
        Sent,
        Skipped,
        Failed
    }

    public readonly struct ReportSendResult
    {
        public ReportSendResult(ReportSendStatus status, string? message)
        {
            Status = status;
            Message = message;
        }

        public ReportSendStatus Status { get; }
        public string? Message { get; }

        public static ReportSendResult Sent() => new ReportSendResult(ReportSendStatus.Sent, null);
        public static ReportSendResult Skipped(string message) => new ReportSendResult(ReportSendStatus.Skipped, message);
        public static ReportSendResult Failed(string message) => new ReportSendResult(ReportSendStatus.Failed, message);
    }
}
