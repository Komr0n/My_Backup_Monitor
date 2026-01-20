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
                
                // Если в режиме FAIL_ONLY и все сервисы OK - не отправляем
                if (config.ReportMode == ReportMode.FailOnly && report.Services.All(s => s.IsValid))
                {
                    return false;
                }

                // Если в режиме OK_ONLY и все сервисы FAIL - не отправляем
                if (config.ReportMode == ReportMode.OkOnly && report.Services.All(s => !s.IsValid))
                {
                    return false;
                }

                var url = string.Format(TelegramApiUrl, config.BotToken);
                
                // Обрабатываем Chat ID - если это число без минуса, но должно быть для группы, пробуем оба варианта
                var chatId = config.ChatId.Trim();
                
                // Если Chat ID выглядит как ID группы (длинное число), но без минуса - добавляем минус
                // ID группы обычно начинается с -100
                if (chatId.StartsWith("100") && chatId.Length > 10 && !chatId.StartsWith("-"))
                {
                    chatId = "-" + chatId;
                }
                
                // Telegram API требует JSON формат
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
                else
                {
                    // Парсим ответ от Telegram API для получения детальной ошибки
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
            }
            catch (Exception)
            {
                throw;
            }
        }

        private string FormatReport(BackupReport report, ReportMode mode)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("<b>📊 Backup Report</b>");
            sb.AppendLine($"Дата: {report.GeneratedAt:dd.MM.yyyy HH:mm}");
            sb.AppendLine();

            foreach (var service in report.Services)
            {
                // Фильтруем по режиму отчёта
                if (mode == ReportMode.FailOnly && service.IsValid)
                    continue;
                if (mode == ReportMode.OkOnly && !service.IsValid)
                    continue;

                if (service.IsValid)
                {
                    sb.AppendLine($"✅ <b>{HtmlEncode(service.Name)}</b> — OK");
                }
                else
                {
                    sb.AppendLine($"❌ <b>{HtmlEncode(service.Name)}</b> — FAIL");
                    
                    if (!string.IsNullOrEmpty(service.ErrorMessage))
                    {
                        sb.AppendLine($"  <i>{HtmlEncode(service.ErrorMessage)}</i>");
                    }
                    else if (service.MissingDates.Count > 0)
                    {
                        foreach (var date in service.MissingDates.OrderBy(d => d))
                        {
                            sb.AppendLine($"  {date:dd.MM.yyyy}");
                        }
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
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
