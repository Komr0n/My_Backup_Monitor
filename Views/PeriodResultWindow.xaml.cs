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
    }
}

