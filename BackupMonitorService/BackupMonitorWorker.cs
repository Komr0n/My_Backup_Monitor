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
        private readonly TelegramCommandBot _commandBot;
        private readonly FileLogger _fileLogger;
        private readonly HashSet<string> _sentTimesToday = new HashSet<string>();
        private readonly object _lockObject = new object();
        private readonly string _stateFilePath;
        private readonly string _heartbeatFilePath;

        public BackupMonitorWorker(
            ILogger<BackupMonitorWorker> logger,
            BackupConfigManager configManager,
            BackupChecker backupChecker,
            TelegramReportSender telegramSender,
            TelegramCommandBot commandBot,
            FileLogger fileLogger)
        {
            _logger = logger;
            _configManager = configManager;
            _backupChecker = backupChecker;
            _telegramSender = telegramSender;
            _commandBot = commandBot;
            _fileLogger = fileLogger ?? throw new ArgumentNullException(nameof(fileLogger));

            var serviceConfigDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BackupMonitorService");
            _stateFilePath = Path.Combine(serviceConfigDir, ".sentstate.json");
            _heartbeatFilePath = Path.Combine(serviceConfigDir, ".heartbeat");

            _logger.LogInformation("BackupMonitorWorker initialized via Dependency Injection.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BackupMonitorService запущен в {time}", DateTimeOffset.Now);

            // Запускаем бот команд в параллельном background-таске.
            // Бот работает постоянно: если команды выключены или нет AllowedChatIds,
            // он ждёт и периодически перепроверяет конфиг, чтобы активироваться
            // сразу после включения без перезапуска службы.
            var botTask = Task.Run(() => RunBotSafelyAsync(stoppingToken), stoppingToken);

            // Восстанавливаем состояние отправленных отчётов с прошлого запуска,
            // чтобы избежать повторной отправки при рестарте службы в минуту отправки.
            LoadSentState();

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

            try
            {
                while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
                {
                    WriteHeartbeat();

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
                                SaveSentState();

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
            finally
            {
                // Останавливаем бот и ждём его завершения
                _commandBot.Stop();
                try { await botTask; }
                catch (OperationCanceledException) { /* ожидаемо */ }
                catch (Exception ex) { _logger.LogWarning(ex, "Ошибка при завершении бота команд"); }
            }
        }

        /// <summary>
        /// Запускает бот команд с устойчивостью к ошибкам: при исключении
        /// перезапускает цикл, пока не запрошена отмена.
        /// </summary>
        private async Task RunBotSafelyAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _commandBot.StartAsync(stoppingToken);
                    return; // бот завершился штатно
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Критическая ошибка бота команд, перезапуск через 10 сек");
                    WriteFileLog($"Ошибка бота команд: {ex.Message}");
                    try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        private bool ShouldSend(DateTime now, string scheduledTime, TimeSpan tolerance)
        {
            // "HH" = 24-часовой формат (0-23). Бывший "hh" был 12-часовым (0-11)
            // — расписание после 12:00 никогда не срабатывало.
            if (!TimeSpan.TryParseExact(scheduledTime, "HH\\:mm", CultureInfo.InvariantCulture, out var scheduled))
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

        /// <summary>
        /// Записывает текущий timestamp в heartbeat-файл. GUI может по нему
        /// определить, жива ли служба (если timestamp старее нескольких минут,
        /// значит служба зависла или не запускалась).
        /// </summary>
        private void WriteHeartbeat()
        {
            try
            {
                var dir = Path.GetDirectoryName(_heartbeatFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_heartbeatFilePath, DateTime.Now.ToString("O"));
            }
            catch
            {
                // heartbeat не критичен
            }
        }

        /// <summary>
        /// Сохраняет список отправленных отчётов (на сегодня) в JSON-файл,
        /// чтобы пережить перезапуск службы.
        /// </summary>
        private void SaveSentState()
        {
            try
            {
                var dir = Path.GetDirectoryName(_stateFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                List<string> snapshot;
                lock (_lockObject)
                {
                    snapshot = new List<string>(_sentTimesToday);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(new { sent = snapshot });
                File.WriteAllText(_stateFilePath, json);
            }
            catch (Exception ex)
            {
                WriteFileLog($"Не удалось сохранить sent-state: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает список отправленных отчётов с прошлого запуска.
        /// Удаляет ключи не за сегодня — они уже не актуальны.
        /// </summary>
        private void LoadSentState()
        {
            try
            {
                if (!File.Exists(_stateFilePath)) return;
                var json = File.ReadAllText(_stateFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("sent", out var sentEl)) return;

                var todayKey = DateTime.Now.ToString("yyyy-MM-dd");
                lock (_lockObject)
                {
                    _sentTimesToday.Clear();
                    foreach (var item in sentEl.EnumerateArray())
                    {
                        var v = item.GetString();
                        if (string.IsNullOrEmpty(v)) continue;
                        // Сохраняем только ключи за сегодня
                        if (v.StartsWith(todayKey) || v == todayKey)
                        {
                            _sentTimesToday.Add(v);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteFileLog($"Не удалось загрузить sent-state: {ex.Message}");
            }
        }

        private void WriteFileLog(string message)
        {
            _fileLogger.Write(message);
        }
    }
}
