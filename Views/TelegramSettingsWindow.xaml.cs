using System;
using System.Linq;
using System.Windows;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Views
{
    public partial class TelegramSettingsWindow : Window
    {
        public TelegramConfig Config { get; private set; }

        public TelegramSettingsWindow(TelegramConfig currentConfig)
        {
            InitializeComponent();
            Config = new TelegramConfig
            {
                Enabled = currentConfig.Enabled,
                BotToken = currentConfig.BotToken,
                ChatId = currentConfig.ChatId,
                ReportMode = currentConfig.ReportMode,
                SendTimes = new System.Collections.Generic.List<string>(currentConfig.SendTimes)
            };
            LoadConfig();
        }

        private void LoadConfig()
        {
            ChkEnabled.IsChecked = Config.Enabled;
            TxtBotToken.Text = Config.BotToken;
            TxtChatId.Text = Config.ChatId;
            
            switch (Config.ReportMode)
            {
                case ReportMode.FailOnly:
                    RbFailOnly.IsChecked = true;
                    break;
                case ReportMode.OkOnly:
                    RbOkOnly.IsChecked = true;
                    break;
                case ReportMode.Full:
                    RbFull.IsChecked = true;
                    break;
            }

            TxtSendTimes.Text = string.Join(Environment.NewLine, Config.SendTimes);
        }

        private void ChkEnabled_Checked(object sender, RoutedEventArgs e)
        {
            UpdateControlsState();
        }

        private void ChkEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateControlsState();
        }

        private void UpdateControlsState()
        {
            bool isEnabled = ChkEnabled.IsChecked == true;
            TxtBotToken.IsEnabled = isEnabled;
            TxtChatId.IsEnabled = isEnabled;
            RbFailOnly.IsEnabled = isEnabled;
            RbOkOnly.IsEnabled = isEnabled;
            RbFull.IsEnabled = isEnabled;
            TxtSendTimes.IsEnabled = isEnabled;
        }

        private void ReportMode_Checked(object sender, RoutedEventArgs e)
        {
            // Обработка выбора типа отчёта
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (ChkEnabled.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(TxtBotToken.Text))
                {
                    MessageBox.Show("Введите Bot Token", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(TxtChatId.Text))
                {
                    MessageBox.Show("Введите Chat ID\n\nДля группы Chat ID должен начинаться с минуса (например: -1003441795211)", 
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Предупреждаем, если Chat ID для группы без минуса
                var chatIdInput = TxtChatId.Text.Trim();
                if (chatIdInput.StartsWith("100") && chatIdInput.Length > 10 && !chatIdInput.StartsWith("-"))
                {
                    var result = MessageBox.Show(
                        $"Chat ID похож на ID группы, но без минуса.\n\nТекущий: {chatIdInput}\nИсправить на: -{chatIdInput}?\n\n(Для групп Chat ID должен начинаться с минуса)", 
                        "Внимание", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        TxtChatId.Text = "-" + chatIdInput;
                    }
                }

                // Проверяем формат времени
                var times = TxtSendTimes.Text
                    .Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                foreach (var time in times)
                {
                    // Проверяем формат HH:mm (24-часовой)
                    var parts = time.Split(':');
                    if (parts.Length != 2 || 
                        !int.TryParse(parts[0], out var hour) || 
                        !int.TryParse(parts[1], out var minute) ||
                        hour < 0 || hour > 23 || 
                        minute < 0 || minute > 59)
                    {
                        MessageBox.Show($"Неверный формат времени: {time}. Используйте формат HH:mm (например, 10:30 или 23:45)", 
                            "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            Config.Enabled = ChkEnabled.IsChecked == true;
            Config.BotToken = TxtBotToken.Text.Trim();
            
            // Обрабатываем Chat ID - для групп должен быть с минусом
            var chatId = TxtChatId.Text.Trim();
            // Если это похоже на ID группы (начинается с 100 и длинное), но без минуса - добавляем
            if (chatId.StartsWith("100") && chatId.Length > 10 && !chatId.StartsWith("-"))
            {
                chatId = "-" + chatId;
            }
            Config.ChatId = chatId;

            if (RbFailOnly.IsChecked == true)
                Config.ReportMode = ReportMode.FailOnly;
            else if (RbOkOnly.IsChecked == true)
                Config.ReportMode = ReportMode.OkOnly;
            else if (RbFull.IsChecked == true)
                Config.ReportMode = ReportMode.Full;

            Config.SendTimes = TxtSendTimes.Text
                .Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

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
