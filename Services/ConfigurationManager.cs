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

                var guiConfigDir = System.AppDomain.CurrentDomain.BaseDirectory;

                if (!ServiceInstallerHelper.IsRunningAsAdministrator())
                {
                    // Запрос согласия на elevation (UAC) для синхронизации конфига
                    var consent = System.Windows.MessageBox.Show(
                        "Служба установлена. Для применения изменений к работающей службе\n" +
                        "нужно обновить конфигурацию в ProgramData (требуются права администратора).\n\n" +
                        "Запросить права администратора сейчас?",
                        "Синхронизация конфигурации",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (consent != System.Windows.MessageBoxResult.Yes)
                    {
                        System.Windows.MessageBox.Show(
                            "Изменения сохранены локально, но служба их не увидит до ручной синхронизации.\n" +
                            "Запустите приложение от имени администратора и сохраните изменения ещё раз.",
                            "Локальное сохранение",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    // Запуск elevated-процесса для выполнения --sync-config
                    try
                    {
                        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = "--sync-config",
                            Verb = "runas",
                            UseShellExecute = true
                        };
                        var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var exited = proc.WaitForExit(15000);
                    if (exited && proc.ExitCode == 0)
                    {
                        System.Windows.MessageBox.Show(
                            "Конфигурация синхронизирована с ProgramData.\n" +
                            "Служба применит изменения в течение минуты.",
                            "Готово",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            "Не удалось синхронизировать конфигурацию.\n" +
                            (exited ? $"Код ошибки: {proc.ExitCode}" : "Процесс не завершился вовремя (timeout)."),
                            "Ошибка",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    }
                }
                return;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Пользователь отменил UAC
                        return;
                    }
                }

                // Уже админ — копируем напрямую
                var result = ServiceInstallerHelper.SyncConfigNow(guiConfigDir);
                if (result == ServiceInstallerHelper.SyncResult.Failed)
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
