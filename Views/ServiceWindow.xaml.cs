using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.Versioning;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Views
{
    [SupportedOSPlatform("windows")]
    public partial class ServiceWindow : Window
    {
        public Service Service { get; private set; }

        public ServiceWindow()
        {
            InitializeComponent();
            Service = new Service();
            LoadDefaultPatterns();
            LoadDefaults();
        }

            public ServiceWindow(Service service)
            {
                if (service == null) throw new ArgumentNullException(nameof(service));

            InitializeComponent();
            Service = new Service
            {
                Name = service.Name,
                Path = service.Path,
                Keywords = new List<string>(service.Keywords ?? new List<string>()),
                DatePatterns = new List<string>(service.DatePatterns ?? new List<string>()),
                ExpectedDayOffset = service.ExpectedDayOffset,
                CheckMode = service.CheckMode,
                FileTimeSource = service.FileTimeSource,
                MinFilesPerDay = service.MinFilesPerDay,
                MinFileSizeBytes = service.MinFileSizeBytes,
                FileMask = service.FileMask,
                Type = service.Type,
                Required = service.Required
            };
            LoadService();
        }

        private void LoadDefaults()
        {
            SelectComboItemByTag(CmbCheckMode, ServiceCheckMode.NameDate.ToString());
            SelectComboItemByTag(CmbFileTimeSource, FileTimeSource.LastWriteTime.ToString());
            TxtExpectedDayOffset.Text = "0";
            TxtMinFilesPerDay.Text = "1";
            TxtMinFileSizeBytes.Text = "0";
            UpdatePanels();
        }

        private void LoadService()
        {
            TxtServiceName.Text = Service.Name;
            TxtPath.Text = Service.Path;
            TxtKeywords.Text = string.Join(", ", Service.Keywords ?? new List<string>());
            TxtDatePatterns.Text = string.Join(Environment.NewLine, Service.DatePatterns ?? new List<string>());
            TxtExpectedDayOffset.Text = Service.ExpectedDayOffset.ToString();
            TxtMinFilesPerDay.Text = Service.MinFilesPerDay.ToString();
            TxtMinFileSizeBytes.Text = Service.MinFileSizeBytes.ToString();
            TxtFileMask.Text = Service.FileMask ?? string.Empty;

            SelectComboItemByTag(CmbCheckMode, Service.CheckMode.ToString());
            SelectComboItemByTag(CmbFileTimeSource, Service.FileTimeSource.ToString());
            UpdatePanels();
        }

        private void LoadDefaultPatterns()
        {
            var defaultPatterns = new[]
            {
                @"(\d{4}_\d{2}_\d{2})",
                @"(\d{4}-\d{2}-\d{2})",
                @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)(\d{8})",
                @"(0[1-9]|[12][0-9]|3[01])(0[1-9]|1[0-2])(20\d{2})",
                @"(20\d{2})(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])"
            };
            TxtDatePatterns.Text = string.Join(Environment.NewLine, defaultPatterns);
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
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
                        // ignore
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void CmbCheckMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePanels();
        }

        private void UpdatePanels()
        {
            var mode = GetSelectedTag(CmbCheckMode);
            var isNameDate = string.Equals(mode, ServiceCheckMode.NameDate.ToString(), StringComparison.OrdinalIgnoreCase);
            RegexPanel.Visibility = isNameDate ? Visibility.Visible : Visibility.Collapsed;
            FileTimePanel.Visibility = isNameDate ? Visibility.Collapsed : Visibility.Visible;
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

            if (!int.TryParse(TxtExpectedDayOffset.Text.Trim(), out var dayOffset) || dayOffset < 0)
            {
                System.Windows.MessageBox.Show("ExpectedDayOffset должен быть числом >= 0", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtMinFilesPerDay.Text.Trim(), out var minFiles) || minFiles <= 0)
            {
                System.Windows.MessageBox.Show("MinFilesPerDay должен быть числом > 0", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(TxtMinFileSizeBytes.Text.Trim(), out var minSizeBytes) || minSizeBytes < 0)
            {
                System.Windows.MessageBox.Show("Минимальный размер файла должен быть числом >= 0 (0 = не проверять)", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var checkModeTag = GetSelectedTag(CmbCheckMode);
            if (!Enum.TryParse<ServiceCheckMode>(checkModeTag, out var checkMode))
            {
                checkMode = ServiceCheckMode.NameDate;
            }

            var fileTimeTag = GetSelectedTag(CmbFileTimeSource);
            if (!Enum.TryParse<FileTimeSource>(fileTimeTag, out var fileTimeSource))
            {
                fileTimeSource = FileTimeSource.LastWriteTime;
            }

            var patterns = TxtDatePatterns.Text
                .Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (checkMode == ServiceCheckMode.NameDate && patterns.Count == 0)
            {
                System.Windows.MessageBox.Show("Укажите хотя бы одно регулярное выражение", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Service.Name = TxtServiceName.Text.Trim();
            Service.Path = TxtPath.Text.Trim();
            Service.Keywords = TxtKeywords.Text.Split(',')
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
            Service.DatePatterns = patterns;
            Service.ExpectedDayOffset = dayOffset;
            Service.CheckMode = checkMode;
            Service.FileTimeSource = fileTimeSource;
            Service.MinFilesPerDay = minFiles;
            Service.MinFileSizeBytes = minSizeBytes;
            Service.FileMask = string.IsNullOrWhiteSpace(TxtFileMask.Text) ? null : TxtFileMask.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string GetSelectedTag(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return tag;
            }
            return string.Empty;
        }

        private static void SelectComboItemByTag(ComboBox comboBox, string tag)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem && string.Equals(comboItem.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }
    }
}
