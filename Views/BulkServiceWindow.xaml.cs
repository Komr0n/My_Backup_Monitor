using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Views
{
    [SupportedOSPlatform("windows")]
    public partial class BulkServiceWindow : Window
    {
        public Service Service { get; private set; }

        public BulkServiceWindow()
        {
            InitializeComponent();
            Service = new Service();
            LoadDefaultPatterns();
            LoadDefaults();
        }

        /// <summary>
        /// Конструктор редактирования существующей группы: поля предзаполняются
        /// значениями группы. Значения по умолчанию НЕ применяются — только реальные.
        /// </summary>
        public BulkServiceWindow(Service existingGroup)
        {
            if (existingGroup == null) throw new ArgumentNullException(nameof(existingGroup));
            InitializeComponent();
            Service = existingGroup;
            Title = "Редактировать группу сервисов";
            LoadExistingGroup(existingGroup);
        }

        private void LoadExistingGroup(Service s)
        {
            TxtGroupName.Text = s.Name;
            TxtBasePath.Text = s.Path;
            TxtChildFolders.Text = string.Join(Environment.NewLine, s.ChildFolders ?? new List<string>());
            TxtKeywords.Text = string.Join(", ", s.Keywords ?? new List<string>());
            ChkUseChildFolderAsKeyword.IsChecked = s.UseChildFolderAsKeyword;
            TxtFileMask.Text = s.FileMask ?? string.Empty;
            TxtDatePatterns.Text = string.Join(Environment.NewLine, s.DatePatterns ?? new List<string>());
            TxtExpectedDayOffset.Text = s.ExpectedDayOffset.ToString();
            TxtMinFilesPerDay.Text = s.MinFilesPerDay.ToString();
            TxtMinFileSizeBytes.Text = s.MinFileSizeBytes.ToString();

            SelectComboItemByTag(CmbCheckMode, s.CheckMode.ToString());
            SelectComboItemByTag(CmbFileTimeSource, s.FileTimeSource.ToString());
            UpdatePanels();
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
                dialog.Description = "Выберите базовую папку с подпапками";
                dialog.ShowNewFolderButton = false;

                if (!string.IsNullOrEmpty(TxtBasePath.Text))
                {
                    try
                    {
                        dialog.SelectedPath = TxtBasePath.Text;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtBasePath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnLoadFolders_Click(object sender, RoutedEventArgs e)
        {
            var basePath = TxtBasePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(basePath))
            {
                MessageBox.Show("Сначала укажите базовый путь", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var folders = Directory.GetDirectories(basePath, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                TxtChildFolders.Text = string.Join(Environment.NewLine, folders);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Нет доступа к папке", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка чтения подпапок: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (string.IsNullOrWhiteSpace(TxtGroupName.Text))
            {
                MessageBox.Show("Введите название группы", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtBasePath.Text))
            {
                MessageBox.Show("Укажите базовый путь", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folders = TxtChildFolders.Text
                .Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (folders.Count == 0)
            {
                MessageBox.Show("Укажите хотя бы одну подпапку", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtExpectedDayOffset.Text.Trim(), out var dayOffset) || dayOffset < 0)
            {
                MessageBox.Show("ExpectedDayOffset должен быть числом >= 0", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtMinFilesPerDay.Text.Trim(), out var minFiles) || minFiles <= 0)
            {
                MessageBox.Show("MinFilesPerDay должен быть числом > 0", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(TxtMinFileSizeBytes.Text.Trim(), out var minSizeBytes) || minSizeBytes < 0)
            {
                MessageBox.Show("Минимальный размер файла должен быть числом >= 0 (0 = не проверять)", "Ошибка",
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
                MessageBox.Show("Укажите хотя бы одно регулярное выражение", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Service = new Service
            {
                Name = TxtGroupName.Text.Trim(),
                Path = TxtBasePath.Text.Trim(),
                Keywords = TxtKeywords.Text.Split(',')
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList(),
                DatePatterns = patterns,
                ExpectedDayOffset = dayOffset,
                CheckMode = checkMode,
                FileTimeSource = fileTimeSource,
                MinFilesPerDay = minFiles,
                MinFileSizeBytes = minSizeBytes,
                FileMask = string.IsNullOrWhiteSpace(TxtFileMask.Text) ? null : TxtFileMask.Text.Trim(),
                Type = ServiceType.Group,
                ChildFolders = folders,
                UseChildFolderAsKeyword = ChkUseChildFolderAsKeyword.IsChecked == true
            };

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
