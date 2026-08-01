using System;
using System.Collections.Generic;
using System.Text;

namespace BackupMonitor.Core.Services
{
    /// <summary>
    /// Общий helper для разбиения длинных Telegram-сообщений на чанки.
    /// Лимит Telegram API — 4096 символов на одно sendMessage; используем
    /// запас в 4000, чтобы умещаться вместе с HTML-разметкой.
    /// Используется и планировщиком отчётов (TelegramReportSender), и
    /// командным ботом (TelegramCommandBot) — единое место логики разбиения.
    /// </summary>
    public static class TelegramMessageFormatter
    {
        /// <summary>
        /// Безопасный лимит на чанк: 4096 (аппаратный лимит Telegram) минус
        /// запас на служебные символы и HTML-теги.
        /// </summary>
        public const int MaxChunkLength = 4000;

        /// <summary>
        /// Разбивает текст на части не длиннее <paramref name="maxChars"/> символов,
        /// стараясь не разрывать отдельные строки. Строки длиннее лимита
        /// жёстко режутся по границе лимита.
        /// </summary>
        /// <param name="text">Исходный текст (любой длины).</param>
        /// <param name="maxChars">Максимальная длина одного чанка. По умолчанию <see cref="MaxChunkLength"/>.</param>
        /// <returns>Список чанков. Пустой список для null/пустого входа.</returns>
        public static List<string> SplitIntoChunks(string? text, int maxChars = MaxChunkLength)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return chunks;
            }

            if (maxChars <= 0)
            {
                maxChars = MaxChunkLength;
            }

            var newLine = Environment.NewLine;
            var current = new StringBuilder();

            foreach (var line in text!.Split('\n'))
            {
                // Если добавление строки переполнит текущий кусок — сбрасываем его.
                // Учитываем длину разделителя строк, который будет добавлен AppendLine.
                if (current.Length > 0
                    && current.Length + line.Length + newLine.Length > maxChars)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }

                if (line.Length > maxChars)
                {
                    // Жёсткий разрез для строк длиннее лимита
                    if (current.Length > 0)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                    }

                    var remaining = line;
                    while (remaining.Length > maxChars)
                    {
                        chunks.Add(remaining.Substring(0, maxChars));
                        remaining = remaining.Substring(maxChars);
                    }
                    current.Append(remaining);
                }
                else
                {
                    current.AppendLine(line);
                }
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
            }

            return chunks;
        }
    }
}
