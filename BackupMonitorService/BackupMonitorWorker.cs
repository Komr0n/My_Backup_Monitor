using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackupMonitor.Core.Models;
using BackupMonitor.Core.Services;
using BackupConfigManager = BackupMonitor.Core.Services.ConfigurationManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackupMonitorService
{
    public class BackupMonitorWorker : BackgroundService
    {
        private readonly ILogger<BackupMonitorWorker> _logger;
        private readonly BackupConfigManager _configManager;
        private readonly BackupChecker _backupChecker;
        private readonly TelegramReportSender _telegramSender;
        private readonly HashSet<string> _sentTimesToday = new HashSet<string>();
        private readonly object _lockObject = new object();
        private readonly string _logFilePath;

        public BackupMonitorWorker(
            ILogger<BackupMonitorWorker> logger,
            BackupConfigManager configManager,
            BackupChecker backupChecker,
            TelegramReportSender telegramSender)
        {
            _logger = logger;
            _configManager = configManager;
            _backupChecker = backupChecker;
            _telegramSender = telegramSender;

            var serviceConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BackupMonitorService");
            _logFilePath = Path.Combine(serviceConfigDir, "service.log");

            _logger.LogInformation("BackupMonitorWorker initialized via Dependency Injection.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackupMonitorService запущен в {time}", DateTimeOffset.Now);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _configManager.ReloadConfiguration();
                    var config = _configManager.TelegramConfig;

                    if (!config.Enabled)
                    {
                        WriteFileLog("Telegram отключен: Enabled=false");
                        continue;
                    }
                    if (config.SendTimes == null || config.SendTimes.Count == 0)
                    {
                        WriteFileLog("Нет времени отправки: SendTimes пуст");
                        continue;
                    }

                    var now = DateTime.Now;
                    var todayKey = now.ToString("yyyy-MM-dd");
                    var tolerance = TimeSpan.FromMinutes(2);

                    lock (_lockObject)
                    {
                        if (!_sentTimesToday.Contains(todayKey))
                        {
                            _sentTimesToday.Clear();
                            _sentTimesToday.Add(todayKey);
                            _logger.LogInformation("Новый день: {todayKey}, сброс списка отправленных отчетов", todayKey);
                            WriteFileLog($"Новый день: {todayKey}");
                        }
                    }

                    foreach (var sendTime in config.SendTimes)
                    {
                        if (string.IsNullOrWhiteSpace(sendTime)) continue;

                        var timeKey = $"{todayKey}_{sendTime}";
                        bool alreadySent;
                        lock (_lockObject)
                        {
                            alreadySent = _sentTimesToday.Contains(timeKey);
                        }

                        if (alreadySent) continue;

                        if (ShouldSend(now, sendTime, tolerance))
                        {
                            _logger.LogInformation("Время отправки наступило: {currentTime} ~ {sendTime}", now.ToString("HH:mm"), sendTime);
                            WriteFileLog($"Отправка по расписанию: now={now:HH:mm:ss}, scheduled={sendTime}");
                            lock (_lockObject)
                            {
                                _sentTimesToday.Add(timeKey);
                            }

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await SendScheduledReportAsync(config);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Ошибка при отправке запланированного отчета");
                                    WriteFileLog($"Ошибка отправки: {ex.Message}");
                                }
                            }, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в основном цикле службы");
                    WriteFileLog($"Ошибка цикла: {ex.Message}");
                }
            }
        }

        private bool ShouldSend(DateTime now, string scheduledTime, TimeSpan tolerance)
        {
            if (!TimeSpan.TryParseExact(scheduledTime, "hh\\:mm", CultureInfo.InvariantCulture, out var scheduled))
            {
                _logger.LogWarning("Неверный формат времени: {sendTime}", scheduledTime);
                WriteFileLog($"Неверный формат времени: {scheduledTime}");
                return false;
            }

            var current = now.TimeOfDay;
            if (current < scheduled)
                return false;

            return (current - scheduled) <= tolerance;
        }

        private async Task SendScheduledReportAsync(TelegramConfig config)
        {
            try
            {
                _logger.LogInformation("Начало отправки запланированного отчета в {time}", DateTime.Now.ToString("HH:mm:ss"));
                var report = await GenerateReportAsync(DateTime.Today);
                if (report == null)
                {
                    _logger.LogWarning("Не удалось сформировать отчет");
                    WriteFileLog("Отчет не сформирован: нет сервисов или ошибка");
                    return;
                }
                var success = await _telegramSender.SendReportAsync(config, report);
                if (success)
                {
                    _logger.LogInformation("Запланированный отчет успешно отправлен в Telegram");
                    WriteFileLog("Отчет отправлен успешно");
                }
                else
                {
                    _logger.LogWarning("Не удалось отправить запланированный отчет (false)");
                    WriteFileLog("Отправка вернула false");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки запланированного отчета: {message}", ex.Message);
                WriteFileLog($"Ошибка отправки: {ex.Message}");
            }
        }

        private async Task<BackupReport?> GenerateReportAsync(DateTime baseDate)
        {
            try
            {
                var report = new BackupReport { GeneratedAt = DateTime.Now };
                var services = _configManager.Services;

                if (services == null || services.Count == 0)
                {
                    _logger.LogWarning("Нет настроенных сервисов для проверки");
                    return null;
                }

                var tasks = services
                    .Select(service => _backupChecker.CheckServiceAsync(service, baseDate))
                    .ToArray();

                var results = await Task.WhenAll(tasks);
                report.Services.AddRange(results);
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при генерации отчета: {message}", ex.Message);
                return null;
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackupMonitorService останавливается");
            WriteFileLog("Служба останавливается");
            await base.StopAsync(stoppingToken);
        }

        private void WriteFileLog(string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
            catch
            {
                // ignore file logging errors
            }
        }
    }
}
