using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Views
{
    public partial class ServiceWindow : Window
    {
        public Service Service { get; private set; }

        public ServiceWindow()
        {
            InitializeComponent();
            Service = new Service();
            LoadDefaultPatterns();
        }

        public ServiceWindow(Service service)
        {
            InitializeComponent();
            // Создаём копию сервиса для редактирования
            Service = new Service
            {
                Name = service.Name,
                Path = service.Path,
                Keywords = new List<string>(service.Keywords),
                DatePatterns = new List<string>(service.DatePatterns)
            };
            LoadService();
        }

        private void LoadService()
        {
            TxtServiceName.Text = Service.Name;
            TxtPath.Text = Service.Path;
            TxtKeywords.Text = string.Join(", ", Service.Keywords);
            TxtDatePatterns.Text = string.Join(Environment.NewLine, Service.DatePatterns);
        }

        private void LoadDefaultPatterns()
        {
            var defaultPatterns = new[]
            {
                @"(\d{4}_\d{2}_\d{2})",           // yyyy_MM_dd (например: 2026_01_01, Arvand_backup_2026_01_01_...)
                @"(\d{4}-\d{2}-\d{2})",           // yyyy-MM-dd (например: 2026-01-06, arvand-2026-01-06-...)
                @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)(\d{8})", // DayOfWeek + MMddyyyy (например: ARVANDFIN_Thu01152026_23361410)
                @"(0[1-9]|[12][0-9]|3[01])(0[1-9]|1[0-2])(20\d{2})",  // ddMMyyyy (например: 01012026, 06012026, 07012026)
                @"(20\d{2})(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])"   // yyyyMMdd (например: 20260106)
            };
            TxtDatePatterns.Text = string.Join(Environment.NewLine, defaultPatterns);
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с бэкапами";
                dialog.ShowNewFolderButton = false;
                
                if (!string.IsNullOrEmpty(TxtPath.Text))
                {
                    try
                    {
                        dialog.SelectedPath = TxtPath.Text;
                    }
                    catch
                    {
                        // Игнорируем ошибку
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtServiceName.Text))
            {
                System.Windows.MessageBox.Show("Введите название сервиса", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtPath.Text))
            {
                System.Windows.MessageBox.Show("Укажите путь к папке с бэкапами", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtKeywords.Text))
            {
                System.Windows.MessageBox.Show("Укажите хотя бы одно ключевое слово", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Service.Name = TxtServiceName.Text.Trim();
            Service.Path = TxtPath.Text.Trim();
            Service.Keywords = TxtKeywords.Text.Split(',')
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
            Service.DatePatterns = TxtDatePatterns.Text
                .Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (Service.DatePatterns.Count == 0)
            {
                System.Windows.MessageBox.Show("Укажите хотя бы одно регулярное выражение", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
