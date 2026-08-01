using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BackupMonitor.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupMonitor.Tests
{
    [TestClass]
    public class TelegramMessageFormatterTests
    {
        /// <summary>
        /// Отчёт с ~50 сервисами должен разбиваться так, чтобы каждый чанк
        /// был не длиннее лимита Telegram (4000 символов, запас от 4096).
        /// Раньше SendReportAsync слал один большой PostAsync и при больших
        /// отчётах Telegram отвергал сообщение целиком.
        /// </summary>
        [TestMethod]
        public void SplitIntoChunks_50Services_EachChunkUnderLimit()
        {
            // Эмулируем большой отчёт: ~80 сервисов с длинными именами и HTML-разметкой.
            // Каждая строка ~90 символов → суммарно >7000, точно больше лимита 4000.
            var sb = new StringBuilder();
            sb.AppendLine("<b>Backup Report</b>");
            sb.AppendLine("Дата: 2026-07-31 10:00");
            sb.AppendLine();
            for (var i = 1; i <= 80; i++)
            {
                sb.AppendLine($"❌ <b>Service-{i:D3}-Production-Database-Server-Farm</b>: FAIL (Нет файлов за период 2026-07-{i:D2})");
            }

            var report = sb.ToString();
            Assert.IsTrue(report.Length > 4000,
                $"Тестовый отчёт ({report.Length} символов) должен превышать лимит одного чанка (4000)");

            var chunks = TelegramMessageFormatter.SplitIntoChunks(report, TelegramMessageFormatter.MaxChunkLength);

            Assert.IsTrue(chunks.Count > 1, "Большой отчёт должен разбиться больше чем на один чанк");

            foreach (var chunk in chunks)
            {
                Assert.IsTrue(chunk.Length <= TelegramMessageFormatter.MaxChunkLength,
                    $"Чанк длиной {chunk.Length} превышает лимит {TelegramMessageFormatter.MaxChunkLength}");
            }

            // Проверяем, что при разбиении все строки сохранены (по содержимому, без учёта разделителей)
            var originalLines = report.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            var chunkLines = chunks.SelectMany(c => c.Split('\n')).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            CollectionAssert.AreEqual(originalLines, chunkLines, "Строки не должны теряться при разбиении");
        }

        [TestMethod]
        public void SplitIntoChunks_EmptyInput_ReturnsEmptyList()
        {
            Assert.AreEqual(0, TelegramMessageFormatter.SplitIntoChunks(null!).Count);
            Assert.AreEqual(0, TelegramMessageFormatter.SplitIntoChunks("").Count);
        }

        [TestMethod]
        public void SplitIntoChunks_ShortMessage_SingleChunk()
        {
            var chunks = TelegramMessageFormatter.SplitIntoChunks("Короткое сообщение");
            Assert.AreEqual(1, chunks.Count);
            Assert.IsTrue(chunks[0].Contains("Короткое сообщение"));
        }

        /// <summary>
        /// Строка длиннее лимита должна жёстко резаться по границе лимита.
        /// </summary>
        [TestMethod]
        public void SplitIntoChunks_LongLine_HardCut()
        {
            var longLine = new string('A', 12000);
            var chunks = TelegramMessageFormatter.SplitIntoChunks(longLine, 4000);

            Assert.IsTrue(chunks.Count >= 3);
            foreach (var chunk in chunks)
            {
                Assert.IsTrue(chunk.Length <= 4000);
            }

            var reassembled = string.Join("", chunks);
            Assert.AreEqual(longLine, reassembled);
        }
    }
}
