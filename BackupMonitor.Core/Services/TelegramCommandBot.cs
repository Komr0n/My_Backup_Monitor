using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Core.Services
{
    /// <summary>
    /// Бот для приёма и обработки команд через Telegram (long polling getUpdates).
    /// Не зависит от Microsoft.Extensions.Logging — логирование выполняется через
    /// переданный делегат Action&lt;string&gt;. Опрос никогда не падает: любые ошибки
    /// логируются, и цикл продолжается.
    /// </summary>
    public class TelegramCommandBot : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly BackupChecker _backupChecker;
        private readonly TelegramReportSender _reportSender;
        private readonly ConfigurationManager _configManager;
        private readonly Action<string>? _log;

        // Шаблон URL: {0} — токен бота, {1} — метод API (sendMessage, getUpdates и т.д.)
        private static readonly string _apiBase = "https://api.telegram.org/bot{0}/{1}";

        // Смещение для getUpdates (long polling): указываем id последнего обработанного обновления + 1
        private long _offset = 0;
        // volatile: пишется из Stop() (поток worker), читается в цикле опроса (поток бота)
        private volatile bool _running = false;
        private CancellationToken _cancellationToken;

        // Счётчик последовательных ошибок для экспоненциального backoff.
        // Сбрасывается при успешном опросе, растёт при ошибках.
        // Пауза: 2s → 4s → 8s → 16s → 32s → 60s (максимум).
        private int _consecutiveErrors = 0;

        /// <summary>
        /// Создаёт экземпляр бота команд.
        /// </summary>
        /// <param name="backupChecker">Служба проверки бэкапов.</param>
        /// <param name="reportSender">Отправитель отчётов (через тот же токен).</param>
        /// <param name="configManager">Менеджер конфигурации (TelegramConfig + Services).</param>
        /// <param name="log">Необязательный делегат логирования.</param>
        public TelegramCommandBot(
            BackupChecker backupChecker,
            TelegramReportSender reportSender,
            ConfigurationManager configManager,
            Action<string>? log = null)
        {
            _backupChecker = backupChecker ?? throw new ArgumentNullException(nameof(backupChecker));
            _reportSender = reportSender ?? throw new ArgumentNullException(nameof(reportSender));
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _log = log;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        /// <summary>
        /// Запускает цикл long polling. Возвращает управление только при остановке
        /// или отмене токена. Если команды выключены или AllowedChatIds пустые,
        /// бот НЕ выходит навсегда — он ждёт и периодически перепроверяет конфиг,
        /// чтобы новые настройки (включение команд, добавление chat_id) вступали
        /// в силу без перезапуска службы.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _running = true;
            _cancellationToken = cancellationToken;

            Log("Telegram command bot loop started (ожидание активации команд...)");

            try
            {
                while (_running && !cancellationToken.IsCancellationRequested)
                {
                    // Перезагружаем конфигурацию в начале каждой итерации, чтобы новые
                    // настройки вступали в силу без перезапуска службы.
                    try
                    {
                        _configManager.ReloadConfiguration();
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка перезагрузки конфигурации: {ex.Message}");
                    }

                    var cfg = _configManager.TelegramConfig;

                    // Если Telegram полностью выключен — ждём
                    if (!cfg.Enabled)
                    {
                        await WaitSafe(10000, cancellationToken);
                        continue;
                    }

                    // Если команды выключены — ждём (не выходим, конфиг может измениться)
                    if (!cfg.EnableCommands)
                    {
                        await WaitSafe(10000, cancellationToken);
                        continue;
                    }

                    // Если токен не настроен — ждём
                    if (string.IsNullOrWhiteSpace(cfg.BotToken))
                    {
                        await WaitSafe(10000, cancellationToken);
                        continue;
                    }

                    // AllowedChatIds пустые — предупреждаем и ждём (не выходим навсегда!)
                    if (cfg.AllowedChatIds == null || cfg.AllowedChatIds.Count == 0)
                    {
                        Log("⚠️ AllowedChatIds не настроены — добавьте chat_id в настройки Telegram");
                        await WaitSafe(15000, cancellationToken);
                        continue;
                    }

                    // Команды активны — опрашиваем обновления
                    var hadError = false;
                    try
                    {
                        await PollOnceAsync();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка при опросе Telegram: {ex.Message}");
                        hadError = true;
                    }

                    // Экспоненциальный backoff при последовательных ошибках:
                    // 2s -> 4s -> 8s -> 16s -> 32s -> 60s (максимум).
                    // При успешном опросе — сброс на стандартные 2s.
                    if (hadError)
                    {
                        _consecutiveErrors++;
                    }
                    else
                    {
                        _consecutiveErrors = 0;
                    }

                    var delay = _consecutiveErrors > 0
                        ? Math.Min(60000, 2000 * (1 << Math.Min(_consecutiveErrors, 5)))
                        : 2000;

                    if (_consecutiveErrors > 1)
                    {
                        Log($"Backoff: {_consecutiveErrors} ошибок подряд, пауза {delay} мс");
                    }

                    // Пауза между опросами, прерываемая отменой
                    await WaitSafe(delay, cancellationToken);
                }
            }
            finally
            {
                _running = false;
                Log("Telegram command bot stopped");
            }
        }

        /// <summary>
        /// Ожидание с поддержкой отмены. Возвращает true, если дождались;
        /// false, если токен отменён.
        /// </summary>
        private async Task WaitSafe(int delayMs, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // ожидаемо при остановке
            }
        }

        /// <summary>
        /// Останавливает цикл опроса (выход произойдёт на следующей итерации).
        /// </summary>
        public void Stop()
        {
            _running = false;
        }

        /// <summary>
        /// Один цикл опроса getUpdates и обработки входящих команд.
        /// </summary>
        private async Task PollOnceAsync()
        {
            var token = _configManager.TelegramConfig.BotToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var url = string.Format(_apiBase, token, $"getUpdates?offset={_offset}&timeout=1");
            var response = await _httpClient.GetAsync(url, _cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(_cancellationToken);
                Log($"getUpdates вернул HTTP {(int)response.StatusCode}: {errorBody}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(_cancellationToken);

            TelegramUpdateResponse? updateResponse;
            try
            {
                updateResponse = JsonSerializer.Deserialize<TelegramUpdateResponse>(json);
            }
            catch (JsonException ex)
            {
                Log($"Не удалось разобрать ответ getUpdates: {ex.Message}");
                return;
            }

            if (updateResponse == null || !updateResponse.Ok || updateResponse.Result == null)
            {
                return;
            }

            foreach (var update in updateResponse.Result)
            {
                // Смещаем offset за пределы обработанного обновления
                _offset = update.UpdateId + 1;

                if (!string.IsNullOrEmpty(update.Message?.Text)
                    && update.Message.Chat != null
                    && update.Message.Chat.Id != 0)
                {
                    var chatId = update.Message.Chat.Id;
                    var text = update.Message.Text;
                    try
                    {
                        await HandleCommandAsync(chatId, text);
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка обработки команды: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Разбирает текст команды и диспетчеризует её.
        /// Чаты, отсутствующие в AllowedChatIds, игнорируются.
        /// </summary>
        private async Task HandleCommandAsync(long chatId, string text)
        {
            var config = _configManager.TelegramConfig;

            // Авторизация чата
            var chatIdStr = chatId.ToString();
            if (config.AllowedChatIds == null
                || !config.AllowedChatIds.Contains(chatIdStr))
            {
                Log($"Unauthorized chat_id: {chatId}");
                return;
            }

            var trimmed = text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];

            switch (command)
            {
                case "/start":
                case "/help":
                    await SendHtmlMessageAsync(chatId, BuildHelpText());
                    break;

                case "/report":
                    await HandleReportCommandAsync(chatId, parts);
                    break;

                case "/services":
                    await SendHtmlMessageAsync(chatId, BuildServicesList());
                    break;

                case "/check":
                    if (parts.Length < 2)
                    {
                        await SendHtmlMessageAsync(chatId, "Использование: /check &lt;имя_сервиса&gt;");
                    }
                    else
                    {
                        // Имя сервиса может содержать пробелы — объединяем всё после команды
                        var name = string.Join(" ", parts.Skip(1));
                        await CheckAndSendSingleAsync(chatId, name);
                    }
                    break;

                default:
                    await SendHtmlMessageAsync(chatId, "Неизвестная команда. Отправьте /help");
                    break;
            }
        }

        /// <summary>
        /// Разбирает подкоманды /report.
        /// </summary>
        private async Task HandleReportCommandAsync(long chatId, string[] parts)
        {
            if (parts.Length == 1)
            {
                // /report — отчёт за сегодня
                await GenerateAndSendTodayAsync(chatId, null);
                return;
            }

            switch (parts[1])
            {
                case "today":
                    await GenerateAndSendTodayAsync(chatId, null);
                    break;

                case "ok":
                    await GenerateAndSendTodayAsync(chatId, "ok");
                    break;

                case "fail":
                    await GenerateAndSendTodayAsync(chatId, "fail");
                    break;

                case "month":
                    // Последние 30 дней
                    await GenerateAndSendPeriodAsync(chatId, DateTime.Today.AddDays(-29), DateTime.Today);
                    break;

                case "period":
                    if (parts.Length < 4)
                    {
                        await SendHtmlMessageAsync(chatId, "Использование: /report period YYYY-MM-DD YYYY-MM-DD");
                    }
                    else if (TryParseDate(parts[2], out var pStart)
                             && TryParseDate(parts[3], out var pEnd))
                    {
                        await GenerateAndSendPeriodAsync(chatId, pStart, pEnd);
                    }
                    else
                    {
                        await SendHtmlMessageAsync(chatId, "Неверный формат даты. Используйте YYYY-MM-DD");
                    }
                    break;

                default:
                    await SendHtmlMessageAsync(chatId, "Неизвестный аргумент /report. Смотрите /help");
                    break;
            }
        }

        /// <summary>
        /// Текст справки по всем поддерживаемым командам.
        /// </summary>
        private static string BuildHelpText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<b>Команды бота мониторинга бэкапов</b>");
            sb.AppendLine();
            sb.AppendLine("/start, /help — эта справка");
            sb.AppendLine("/report — отчёт за сегодня");
            sb.AppendLine("/report today — отчёт за сегодня");
            sb.AppendLine("/report ok — только OK за сегодня");
            sb.AppendLine("/report fail — только ошибки за сегодня");
            sb.AppendLine("/report month — за последние 30 дней");
            sb.AppendLine("/report period YYYY-MM-DD YYYY-MM-DD — произвольный период");
            sb.AppendLine("/services — список настроенных сервисов");
            sb.AppendLine("/check &lt;имя&gt; — проверить один сервис");
            return sb.ToString();
        }

        /// <summary>
        /// Список настроенных сервисов (имена).
        /// </summary>
        private string BuildServicesList()
        {
            var services = _configManager.Services;
            if (services == null || services.Count == 0)
            {
                return "Сервисы не настроены.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("<b>Настроенные сервисы:</b>");
            sb.AppendLine();
            foreach (var s in services)
            {
                sb.AppendLine($"• {HtmlEncode(s.Name)}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Проверяет один сервис и отправляет результат.
        /// </summary>
        private async Task CheckAndSendSingleAsync(long chatId, string name)
        {
            var service = _configManager.Services?.FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (service == null)
            {
                await SendHtmlMessageAsync(chatId, $"Сервис не найден: {HtmlEncode(name)}");
                return;
            }

            ServiceCheckResult result;
            try
            {
                result = await _backupChecker.CheckServiceAsync(service, DateTime.Today);
            }
            catch (Exception ex)
            {
                await SendHtmlMessageAsync(chatId, $"Ошибка проверки: {HtmlEncode(ex.Message)}");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<b>Проверка сервиса</b>");
            sb.AppendLine($"Дата: {DateTime.Today:dd.MM.yyyy}");
            sb.AppendLine();

            var emoji = GetStatusEmoji(result.Status);
            sb.AppendLine($"{emoji} <b>{HtmlEncode(result.ServiceName)}</b>: {HtmlEncode(result.Status.ToString())}");
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                sb.AppendLine($"({HtmlEncode(result.Message)})");
            }
            sb.AppendLine($"Файлов найдено: {result.FoundCount} / {result.MinRequiredCount}");

            if (result.Details != null && result.Details.Count > 0)
            {
                sb.AppendLine("<blockquote>");
                foreach (var d in result.Details)
                {
                    sb.AppendLine($"<i>{HtmlEncode(d)}</i>");
                }
                sb.AppendLine("</blockquote>");
            }

            await SendHtmlMessageAsync(chatId, sb.ToString());
        }

        /// <summary>
        /// Формирует и отправляет отчёт за сегодня по всем сервисам.
        /// </summary>
        /// <param name="chatId">Целевой чат.</param>
        /// <param name="filter">"ok" — только OK; "fail" — только не-OK; null — без фильтра.</param>
        private async Task GenerateAndSendTodayAsync(long chatId, string? filter)
        {
            var services = _configManager.Services ?? new List<Service>();
            var report = new BackupReport
            {
                GeneratedAt = DateTime.Now
            };

            foreach (var service in services)
            {
                try
                {
                    var result = await _backupChecker.CheckServiceAsync(service, DateTime.Today);
                    report.Services.Add(result);
                }
                catch (Exception ex)
                {
                    report.Services.Add(new ServiceCheckResult
                    {
                        ServiceName = service.Name,
                        Status = ServiceCheckStatus.ERROR,
                        Message = ex.Message
                    });
                }
            }

            // Применяем фильтр к результатам
            IEnumerable<ServiceCheckResult> filtered = report.Services;
            if (filter == "ok")
            {
                filtered = filtered.Where(r => r.Status == ServiceCheckStatus.OK);
            }
            else if (filter == "fail")
            {
                filtered = filtered.Where(r => r.Status != ServiceCheckStatus.OK);
            }

            var list = filtered.ToList();

            var okCount = list.Count(r => r.Status == ServiceCheckStatus.OK);
            var warnCount = list.Count(r => r.Status == ServiceCheckStatus.WARNING);
            var failCount = list.Count(r => r.Status == ServiceCheckStatus.FAIL);
            var errorCount = list.Count(r => r.Status == ServiceCheckStatus.ERROR);

            var sb = new StringBuilder();
            sb.AppendLine("<b>Отчёт за сегодня</b>");
            sb.AppendLine($"Дата: {report.GeneratedAt:dd.MM.yyyy HH:mm}");
            sb.AppendLine($"OK: {okCount} | WARNING: {warnCount} | FAIL: {failCount} | ERROR: {errorCount}");
            sb.AppendLine();

            if (list.Count == 0)
            {
                sb.AppendLine("<i>Нет данных по заданному фильтру</i>");
            }
            else
            {
                foreach (var r in list)
                {
                    var emoji = GetStatusEmoji(r.Status);
                    var line = $"{emoji} <b>{HtmlEncode(r.ServiceName)}</b>: {HtmlEncode(r.Status.ToString())}";
                    if (!string.IsNullOrWhiteSpace(r.Message))
                    {
                        line += $" ({HtmlEncode(r.Message)})";
                    }
                    sb.AppendLine(line);
                }
            }

            await SendHtmlMessageAsync(chatId, sb.ToString());
        }

        /// <summary>
        /// Формирует и отправляет сводку по периоду. Поддерживаются как одиночные,
        /// так и групповые сервисы — для групп CheckBackupForPeriod делает fan-out
        /// по дочерним сервисам и агрегирует результат.
        /// </summary>
        private async Task GenerateAndSendPeriodAsync(long chatId, DateTime start, DateTime end)
        {
            var services = _configManager.Services ?? new List<Service>();

            var rows = new List<(string Name, bool Valid, int Missing, string Error)>();

            foreach (var service in services)
            {
                try
                {
                    var check = _backupChecker.CheckBackupForPeriod(service, start, end);
                    var missing = check.MissingDates?.Count ?? 0;
                    rows.Add((service.Name, check.IsValid, missing, check.ErrorMessage));
                }
                catch (Exception ex)
                {
                    rows.Add((service.Name, false, 0, ex.Message));
                }
            }

            var totalServices = rows.Count;
            var validCount = rows.Count(r => r.Valid);
            var withMissingCount = rows.Count(r => !r.Valid && r.Missing > 0);

            var sb = new StringBuilder();
            sb.AppendLine("<b>Отчёт за период</b>");
            sb.AppendLine($"Период: {start:dd.MM.yyyy} — {end:dd.MM.yyyy}");
            sb.AppendLine($"Всего сервисов: {totalServices} | Корректных: {validCount} | С пропусками: {withMissingCount}");
            sb.AppendLine();

            if (rows.Count == 0)
            {
                sb.AppendLine("<i>Нет сервисов для проверки</i>");
            }
            else
            {
                foreach (var row in rows)
                {
                    // OK → зелёный; есть пропущенные дни → красный; прочие ошибки — 🔥
                    var emoji = row.Valid
                        ? "✅"
                        : (row.Missing > 0 ? "❌" : "🔥");

                    var detail = row.Valid
                        ? string.Empty
                        : (row.Missing > 0
                            ? $" (пропущено дней: {row.Missing})"
                            : (!string.IsNullOrEmpty(row.Error) ? $" ({HtmlEncode(row.Error)})" : string.Empty));

                    sb.AppendLine($"{emoji} <b>{HtmlEncode(row.Name)}</b>{detail}");
                }
            }

            await SendHtmlMessageAsync(chatId, sb.ToString());
        }

        /// <summary>
        /// Отправляет HTML-сообщение в чат. Сообщения длиннее 4000 символов
        /// разбиваются по строкам на части (общий helper TelegramMessageFormatter).
        /// </summary>
        private async Task SendHtmlMessageAsync(long chatId, string text)
        {
            var token = _configManager.TelegramConfig.BotToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var url = string.Format(_apiBase, token, "sendMessage");
            var chunks = TelegramMessageFormatter.SplitIntoChunks(text);

            foreach (var chunk in chunks)
            {
                var payload = new
                {
                    chat_id = chatId.ToString(),
                    text = chunk,
                    parse_mode = "HTML"
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync(url, content, _cancellationToken);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(_cancellationToken);
                    Log($"Не удалось отправить сообщение (HTTP {(int)response.StatusCode}): {body}");
                }
            }
        }

        /// <summary>
        /// Разбирает дату в формате YYYY-MM-DD.
        /// </summary>
        private static bool TryParseDate(string value, out DateTime result)
        {
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result);
        }

        private static string GetStatusEmoji(ServiceCheckStatus status)
        {
            return status switch
            {
                ServiceCheckStatus.OK => "✅",
                ServiceCheckStatus.WARNING => "⚠️",
                ServiceCheckStatus.FAIL => "❌",
                ServiceCheckStatus.ERROR => "🔥",
                _ => "❓"
            };
        }

        private static string HtmlEncode(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private void Log(string message)
        {
            _log?.Invoke(message);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
