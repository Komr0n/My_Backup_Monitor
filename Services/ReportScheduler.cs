using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Services
{
    public class ReportScheduler
    {
        private readonly DispatcherTimer _timer;
        private readonly ConfigurationManager _configManager;
        private readonly BackupChecker _backupChecker;
        private readonly TelegramReportSender _telegramSender;
        private readonly HashSet<string> _sentTimesToday = new HashSet<string>();

        public ReportScheduler(ConfigurationManager configManager, BackupChecker backupChecker, TelegramReportSender telegramSender)
        {
            _configManager = configManager;
            _backupChecker = backupChecker;
            _telegramSender = telegramSender;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(30);
            _timer.Tick += Timer_Tick;
        }

        public void Start()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                System.Diagnostics.Debug.WriteLine("Планировщик отчетов запущен");
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

            if (!_sentTimesToday.Contains(todayKey))
            {
                _sentTimesToday.Clear();
                _sentTimesToday.Add(todayKey);
                System.Diagnostics.Debug.WriteLine($"Новый день: {todayKey}, сброс списка отправленных отчетов");
            }

            foreach (var sendTime in config.SendTimes)
            {
                var timeKey = $"{todayKey}_{sendTime}";

                if (_sentTimesToday.Contains(timeKey))
                    continue;

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
            var currentParts = currentTime.Split(':');
            var scheduledParts = scheduledTime.Split(':');

            if (currentParts.Length == 2 && scheduledParts.Length == 2)
            {
                if (int.TryParse(currentParts[0], out var currentHour) &&
                    int.TryParse(currentParts[1], out var currentMinute) &&
                    int.TryParse(scheduledParts[0], out var scheduledHour) &&
                    int.TryParse(scheduledParts[1], out var scheduledMinute))
                {
                    return currentHour == scheduledHour && currentMinute == scheduledMinute;
                }
            }
            return false;
        }

        private async Task SendScheduledReportAsync(TelegramConfig config)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Начало отправки запланированного отчета в {DateTime.Now:HH:mm:ss}");
                var report = await GenerateReportAsync(DateTime.Today);
                var preSendResult = GetPreSendResult(config, report);
                if (preSendResult.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine($"Отчет не отправлен: {preSendResult.Value.Message}");
                    return;
                }

                var success = await _telegramSender.SendReportAsync(config, report);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Запланированный отчет успешно отправлен");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Не удалось отправить запланированный отчет (false)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки запланированного отчета: {ex.Message}");
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

            var report = await GenerateReportAsync(DateTime.Today);
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

        private ReportSendResult? GetPreSendResult(TelegramConfig config, BackupReport report)
        {
            if (report.Services.Count == 0)
            {
                return ReportSendResult.Failed("Нет настроенных сервисов для отчета");
            }

            var leafResults = FlattenLeafResults(report.Services).ToList();
            if (config.ReportMode == ReportMode.FailOnly && leafResults.All(r => r.Status == ServiceCheckStatus.OK))
            {
                return ReportSendResult.Skipped("Все сервисы в статусе OK. Режим FAIL_ONLY");
            }

            if (config.ReportMode == ReportMode.OkOnly && leafResults.All(r => r.Status != ServiceCheckStatus.OK))
            {
                return ReportSendResult.Skipped("Все сервисы в статусе FAIL. Режим OK_ONLY");
            }

            return null;
        }

        private async Task<BackupReport> GenerateReportAsync(DateTime baseDate)
        {
            var report = new BackupReport
            {
                GeneratedAt = DateTime.Now
            };

            var tasks = _configManager.Services
                .Select(service => _backupChecker.CheckServiceAsync(service, baseDate))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            report.Services.AddRange(results);

            return report;
        }

        private static IEnumerable<ServiceCheckResult> FlattenLeafResults(IEnumerable<ServiceCheckResult> results)
        {
            foreach (var result in results)
            {
                if (result.Children != null && result.Children.Count > 0)
                {
                    foreach (var child in FlattenLeafResults(result.Children))
                    {
                        yield return child;
                    }
                }
                else
                {
                    yield return result;
                }
            }
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
