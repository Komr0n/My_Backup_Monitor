using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BackupMonitor.Core.Services;
using BackupMonitor.Services;

namespace BackupMonitor.Views
{
    public partial class PeriodResultWindow : Window
    {
        public PeriodResultWindow(string? serviceName, DateTime start, DateTime end, BackupMonitor.Core.Services.BackupChecker.CheckResult result)
        {
            InitializeComponent();

            var displayName = serviceName ?? "Неизвестный сервис";
            HeaderText.Text = $"{displayName}: {start:yyyy-MM-dd} - {end:yyyy-MM-dd}";

            // Если это группа — показываем сводку по каждому дочернему сервису.
            if (result.ChildResults != null && result.ChildResults.Count > 0)
            {
                var rows = result.ChildResults
                    .Select(kv =>
                    {
                        var ok = string.IsNullOrEmpty(kv.Value.ErrorMessage) && kv.Value.IsValid;
                        var summary = !string.IsNullOrEmpty(kv.Value.ErrorMessage)
                            ? kv.Value.ErrorMessage
                            : (kv.Value.IsValid
                                ? "OK (пропусков нет)"
                                : $"пропущено {kv.Value.MissingDates.Count} дн.");
                        return new ChildRow { Name = kv.Key, Summary = (ok ? "✅ " : "❌ ") + summary };
                    })
                    .OrderBy(r => r.Name)
                    .ToList();

                ChildrenList.ItemsSource = rows;
                ChildrenPanel.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                StatusText.Text = result.ErrorMessage;
                MissingList.ItemsSource = Array.Empty<string>();
                return;
            }

            if (result.MissingDates.Count == 0)
            {
                StatusText.Text = "Все бэкапы найдены";
                MissingList.ItemsSource = new List<string> { "Нет пропусков" };
            }
            else
            {
                StatusText.Text = $"Отсутствуют бэкапы за {result.MissingDates.Count} дн.";
                MissingList.ItemsSource = result.MissingDates
                    .OrderBy(d => d)
                    .Select(d => d.ToString("yyyy-MM-dd"))
                    .ToList();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        /// <summary>
        /// Одна строка в сводке дочерних сервисов группы.
        /// </summary>
        public sealed class ChildRow
        {
            public string Name { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
        }
    }
}

