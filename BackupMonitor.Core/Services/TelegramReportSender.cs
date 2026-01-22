using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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

            try
            {
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

                var payload = new
                {
                    chat_id = chatId,
                    text = message,
                    parse_mode = "HTML"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                try
                {
                    var errorResponse = System.Text.Json.JsonSerializer.Deserialize<TelegramErrorResponse>(responseContent);
                    var errorMessage = errorResponse?.description ?? responseContent;
                    throw new Exception($"Telegram API: {errorMessage}");
                }
                catch
                {
                    throw new Exception($"HTTP {response.StatusCode}: {responseContent}");
                }
            }
            catch (Exception)
            {
                throw;
            }
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

            var statusText = result.Status.ToString();
            var emoji = GetStatusEmoji(result.Status);
            var line = $"{emoji} <b>{HtmlEncode(result.ServiceName)}</b>: {HtmlEncode(statusText)}";
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                line += $" ({HtmlEncode(result.Message)})";
            }
            sb.AppendLine(line);

            if (result.Children != null && result.Children.Count > 0)
            {
                sb.AppendLine("<blockquote>");
                foreach (var child in result.Children)
                {
                    AppendChildLine(sb, child);
                }
                sb.AppendLine("</blockquote>");
            }
            else if (result.Details != null && result.Details.Count > 0)
            {
                sb.AppendLine("<blockquote>");
                foreach (var detail in result.Details)
                {
                    sb.AppendLine($"<i>{HtmlEncode(detail)}</i>");
                }
                sb.AppendLine("</blockquote>");
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
