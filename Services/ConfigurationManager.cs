// Этот файл оставлен для обратной совместимости, но теперь использует BackupMonitor.Core
// Все основные методы делегируются в BackupMonitor.Core.Services.ConfigurationManager
using BackupMonitor.Core.Services;

namespace BackupMonitor.Services
{
    // Обёртка над BackupMonitor.Core.Services.ConfigurationManager для WPF
    // с поддержкой MessageBox для показа ошибок
    public class ConfigurationManager
    {
        private readonly BackupMonitor.Core.Services.ConfigurationManager _coreManager;

        public ConfigurationManager()
        {
            var configDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
            _coreManager = new BackupMonitor.Core.Services.ConfigurationManager(configDirectory);
        }

        public ConfigurationManager(string? configDirectory)
        {
            _coreManager = new BackupMonitor.Core.Services.ConfigurationManager(configDirectory);
        }

        public System.Collections.Generic.List<BackupMonitor.Core.Models.Service> Services => _coreManager.Services;
        public BackupMonitor.Core.Models.TelegramConfig TelegramConfig => _coreManager.TelegramConfig;

        public void LoadConfiguration() => _coreManager.LoadConfiguration();
        public void LoadTelegramConfig() => _coreManager.LoadTelegramConfig();
        public void SaveConfiguration() => _coreManager.SaveConfiguration();
        public void SaveTelegramConfig() => _coreManager.SaveTelegramConfig();

        public void SaveConfigurationAndSync()
        {
            try
            {
                _coreManager.SaveConfiguration();
                TrySyncServiceConfig();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сохранения конфигурации: {ex.Message}", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void UpdateTelegramConfig(BackupMonitor.Core.Models.TelegramConfig config)
        {
            try
            {
                _coreManager.UpdateTelegramConfig(config);
                TrySyncServiceConfig();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сохранения конфигурации Telegram: {ex.Message}", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void AddService(BackupMonitor.Core.Models.Service service)
        {
            try
            {
                _coreManager.AddService(service);
                TrySyncServiceConfig();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сохранения конфигурации: {ex.Message}", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void UpdateService(int index, BackupMonitor.Core.Models.Service service)
        {
            try
            {
                _coreManager.UpdateService(index, service);
                TrySyncServiceConfig();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сохранения конфигурации: {ex.Message}", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void RemoveService(int index)
        {
            try
            {
                _coreManager.RemoveService(index);
                TrySyncServiceConfig();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сохранения конфигурации: {ex.Message}", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void TrySyncServiceConfig()
        {
            try
            {
                var serviceManager = new WindowsServiceManager();
                if (!serviceManager.IsServiceInstalled())
                {
                    return;
                }

                if (!ServiceInstallerHelper.IsRunningAsAdministrator())
                {
                    System.Windows.MessageBox.Show(
                        "Служба установлена, но ее конфигурация хранится в ProgramData.\n" +
                        "Для обновления конфигурации запустите приложение от имени администратора и сохраните изменения еще раз.",
                        "Требуются права администратора",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }

                var guiConfigDir = System.AppDomain.CurrentDomain.BaseDirectory;
                var copied = ServiceInstallerHelper.CopyConfigFilesToConfigDir(guiConfigDir);
                if (!copied)
                {
                    System.Windows.MessageBox.Show(
                        "Не удалось обновить конфигурацию службы. Проверьте права доступа и повторите попытку.",
                        "Ошибка",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Ошибка синхронизации конфигурации службы: {ex.Message}",
                    "Ошибка",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
