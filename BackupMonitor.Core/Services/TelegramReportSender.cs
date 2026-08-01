using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Core.Services
{
    public class TelegramReportSender : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string TelegramApiUrl = "https://api.telegram.org/bot{0}/sendMessage";

        public TelegramReportSender()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<bool> SendReportAsync(TelegramConfig config, BackupReport report)
        {
            if (!config.Enabled || string.IsNullOrWhiteSpace(config.BotToken) || string.IsNullOrWhiteSpace(config.ChatId))
            {
                return false;
            }

            var message = FormatReport(report, config.ReportMode);

            if (config.ReportMode == ReportMode.FailOnly && FlattenLeafResults(report.Services).All(IsOk))
            {
                return false;
            }

            if (config.ReportMode == ReportMode.OkOnly && FlattenLeafResults(report.Services).All(s => !IsOk(s)))
            {
                return false;
            }

            var url = string.Format(TelegramApiUrl, config.BotToken);
            var chatId = config.ChatId.Trim();

            if (chatId.StartsWith("100") && chatId.Length > 10 && !chatId.StartsWith("-"))
            {
                chatId = "-" + chatId;
            }

            // Разбиваем длинный отчёт на чанки ≤ 4000 символов (лимит Telegram — 4096).
            // Раньше отправляли одним запросом — большие отчёты (>4096) отвергались API.
            var chunks = TelegramMessageFormatter.SplitIntoChunks(message);
            var anySent = false;
            var sentChunks = 0;

            foreach (var chunk in chunks)
            {
                var payload = new
                {
                    chat_id = chatId,
                    text = chunk,
                    parse_mode = "HTML"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Десериализуем тело ошибки Telegram; если оно не парсится —
                    // показываем сырой ответ. try/catch только вокруг десериализации.
                    string errorMessage;
                    try
                    {
                        var errorResponse = System.Text.Json.JsonSerializer.Deserialize<TelegramErrorResponse>(responseContent);
                        errorMessage = !string.IsNullOrWhiteSpace(errorResponse?.description)
                            ? errorResponse.description!
                            : responseContent;
                    }
                    catch (JsonException)
                    {
                        errorMessage = responseContent;
                    }

                    // Если часть чанков уже доставлена, сообщаем об этом — иначе
                    // пользователь увидит "ошибку" при фактически частично отправленном отчёте.
                    var partial = sentChunks > 0
                        ? $" (уже отправлено чанков: {sentChunks}/{chunks.Count})"
                        : string.Empty;
                    throw new Exception(
                        $"Telegram API (HTTP {(int)response.StatusCode}): {errorMessage}{partial}");
                }

                anySent = true;
                sentChunks++;
            }

            return anySent;
        }

        private string FormatReport(BackupReport report, ReportMode mode)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<b>Backup Report</b>");
            sb.AppendLine($"Дата: {report.GeneratedAt:dd.MM.yyyy HH:mm}");
            sb.AppendLine();

            var leafResults = FlattenLeafResults(report.Services).ToList();
            var okCount = leafResults.Count(r => r.Status == ServiceCheckStatus.OK);
            var warningCount = leafResults.Count(r => r.Status == ServiceCheckStatus.WARNING);
            var failCount = leafResults.Count(r => r.Status == ServiceCheckStatus.FAIL);
            var errorCount = leafResults.Count(r => r.Status == ServiceCheckStatus.ERROR);

            sb.AppendLine($"OK: {okCount} | WARNING: {warningCount} | FAIL: {failCount} | ERROR: {errorCount}");
            sb.AppendLine();

            foreach (var service in report.Services)
            {
                AppendServiceDetails(sb, service, mode);
            }

            return sb.ToString();
        }

        private void AppendServiceDetails(StringBuilder sb, ServiceCheckResult result, ReportMode mode)
        {
            var isOk = result.Status == ServiceCheckStatus.OK;
            if (mode == ReportMode.FailOnly && isOk)
                return;
            if (mode == ReportMode.OkOnly && !isOk)
                return;

            var isGroup = result.Children != null && result.Children.Count > 0;

            if (isGroup)
            {
                // Заголовок группы с пометкой и сводкой
                var children = result.Children!;
                var total = children.Count;
                var okChildren = children.Count(c => c.Status == ServiceCheckStatus.OK);
                var failChildren = children.Count(c => c.Status == ServiceCheckStatus.FAIL);
                var errorChildren = children.Count(c => c.Status == ServiceCheckStatus.ERROR);

                var groupEmoji = GetStatusEmoji(result.Status);
                var groupLabel = $"📁 <b>Группа «{HtmlEncode(result.ServiceName)}»</b>: ";
                if (okChildren == total)
                    groupLabel += $"все OK ({total}/{total})";
                else
                    groupLabel += $"OK: {okChildren} | FAIL: {failChildren} | ERROR: {errorChildren} из {total}";
                sb.AppendLine(groupLabel);

                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    sb.AppendLine($"   <i>{HtmlEncode(result.Message)}</i>");
                }

                sb.AppendLine("<blockquote>");
                foreach (var child in children)
                {
                    AppendChildLine(sb, child);
                }
                sb.AppendLine("</blockquote>");
            }
            else
            {
                var statusText = result.Status.ToString();
                var emoji = GetStatusEmoji(result.Status);
                var line = $"{emoji} <b>{HtmlEncode(result.ServiceName)}</b>: {HtmlEncode(statusText)}";
                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    line += $" ({HtmlEncode(result.Message)})";
                }
                sb.AppendLine(line);

                if (result.Details != null && result.Details.Count > 0)
                {
                    sb.AppendLine("<blockquote>");
                    foreach (var detail in result.Details)
                    {
                        sb.AppendLine($"<i>{HtmlEncode(detail)}</i>");
                    }
                    sb.AppendLine("</blockquote>");
                }
            }
        }

        private void AppendChildLine(StringBuilder sb, ServiceCheckResult result)
        {
            var statusText = result.Status.ToString();
            var emoji = GetStatusEmoji(result.Status);
            var line = $"{emoji} {HtmlEncode(result.ServiceName)}: {HtmlEncode(statusText)}";
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                line += $" ({HtmlEncode(result.Message)})";
            }
            sb.AppendLine(line);

            if (result.Details != null && result.Details.Count > 0)
            {
                foreach (var detail in result.Details)
                {
                    sb.AppendLine($"<i>{HtmlEncode(detail)}</i>");
                }
            }
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

        private static bool IsOk(ServiceCheckResult result)
        {
            return result.Status == ServiceCheckStatus.OK;
        }

        private static string HtmlEncode(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private class TelegramErrorResponse
        {
            public bool ok { get; set; }
            public int error_code { get; set; }
            public string? description { get; set; }
        }
    }
}
