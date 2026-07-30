using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BackupMonitor.Core.Models
{
    /// <summary>
    /// Ответ Telegram Bot API на запрос getUpdates.
    /// Используется приёмником команд (long polling).
    /// </summary>
    public class TelegramUpdateResponse
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("result")] public List<TelegramUpdate> Result { get; set; } = new();
    }

    /// <summary>
    /// Одно обновление Telegram (входящее сообщение, callback и т.д.).
    /// Для приёма команд используется только поле Message.
    /// </summary>
    public class TelegramUpdate
    {
        [JsonPropertyName("update_id")] public long UpdateId { get; set; }
        [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    }

    /// <summary>
    /// Входящее сообщение Telegram.
    /// </summary>
    public class TelegramMessage
    {
        [JsonPropertyName("message_id")] public long MessageId { get; set; }
        [JsonPropertyName("from")] public TelegramUser? From { get; set; }
        [JsonPropertyName("chat")] public TelegramChat? Chat { get; set; }
        [JsonPropertyName("date")] public long Date { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    /// <summary>
    /// Отправитель сообщения (пользователь или бот).
    /// </summary>
    public class TelegramUser
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("is_bot")] public bool IsBot { get; set; }
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
    }

    /// <summary>
    /// Чат Telegram (личный, группа, канал). Id используется как получатель ответа.
    /// </summary>
    public class TelegramChat
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
    }
}
