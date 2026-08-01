using System;
using System.Collections.Generic;
using System.IO;
using BackupMonitor.Core.Models;
using Newtonsoft.Json;

namespace BackupMonitor.Core.Services
{
    public class ConfigurationManager
    {
        private readonly string _configDirectory;
        private const string ConfigFileName = "services.json";
        private const string AppConfigFileName = "appconfig.json";
        private List<Service> _services = new List<Service>();
        private List<Service>? _lastValidServices;
        private TelegramConfig _telegramConfig = new TelegramConfig();

        public List<Service> Services => _services;
        public TelegramConfig TelegramConfig => _telegramConfig;

        public ConfigurationManager(string? configDirectory = null)
        {
            // Определяем директорию конфигурации
            // Если не указана, используем директорию приложения
            _configDirectory = configDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            LoadConfiguration();
            LoadTelegramConfig();
        }

        /// <summary>
        /// Возвращает список директорий, где может лежать конфиг, в порядке приоритета.
        /// Основная (_configDirectory) — первая, затем fallback-варианты.
        /// </summary>
        private IEnumerable<string> GetConfigSearchPaths()
        {
            // 1. Основная директория (ProgramData для службы, BaseDirectory для GUI)
            yield return _configDirectory;

            // 2. Директория приложения (BaseDirectory) — если основная другая
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.Equals(baseDir, _configDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield return baseDir;
            }

            // 3. ProgramData\BackupMonitorService — для GUI при поиске конфига службы
            var programData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "BackupMonitorService");
            if (!string.Equals(programData, _configDirectory, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(programData, baseDir, StringComparison.OrdinalIgnoreCase))
            {
                yield return programData;
            }
        }

        private string? ResolveConfigFile(string fileName)
        {
            foreach (var dir in GetConfigSearchPaths())
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        public void LoadConfiguration()
        {
            try
            {
                var configPath = ResolveConfigFile(ConfigFileName);
                if (configPath != null)
                {
                    var json = File.ReadAllText(configPath);
                    var loaded = JsonConvert.DeserializeObject<List<Service>>(json);
                    // Сохраняем предыдущий список, чтобы при ошибке десериализации
                    // (например, файл пишется GUI в этот момент) не потерять сервисы.
                    _services = loaded ?? new List<Service>();
                    _lastValidServices = new List<Service>(_services);
                }
                else
                {
                    _services = new List<Service>();
                    _lastValidServices = new List<Service>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки конфигурации: {ex.Message}");
                // Не затираем предыдущий успешный список — лучше работать со старым,
                // чем молча обнулить все сервисы.
                if (_lastValidServices != null)
                {
                    _services = new List<Service>(_lastValidServices);
                }
                else if (_services == null)
                {
                    _services = new List<Service>();
                }
            }
        }

        public void LoadTelegramConfig()
        {
            try
            {
                var appConfigPath = ResolveConfigFile(AppConfigFileName);
                if (appConfigPath != null)
                {
                    var json = File.ReadAllText(appConfigPath);
                    var appConfig = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (appConfig?.Telegram != null)
                    {
                        _telegramConfig = appConfig.Telegram;

                        // Исправляем Chat ID при загрузке, если нужно (только в памяти,
                        // без записи на диск — LoadTelegramConfig вызывается каждые 2-10 сек
                        // ботом/worker, а запись при каждой загрузке будет перезаписывать
                        // конфиг и мешать параллельным изменениям GUI).
                        if (!string.IsNullOrEmpty(_telegramConfig.ChatId))
                        {
                            var chatId = _telegramConfig.ChatId.Trim();
                            if (chatId.StartsWith("100") && chatId.Length > 10 && !chatId.StartsWith("-"))
                            {
                                _telegramConfig.ChatId = "-" + chatId;
                            }
                        }
                    }
                }
                else
                {
                    // Значения по умолчанию
                    _telegramConfig = new TelegramConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки конфигурации Telegram: {ex.Message}");
                _telegramConfig = new TelegramConfig();
            }
        }

        public void SaveConfiguration()
        {
            try
            {
                var configPath = Path.Combine(_configDirectory, ConfigFileName);
                var json = JsonConvert.SerializeObject(_services, Formatting.Indented);
                WriteAllTextAtomic(configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения конфигурации: {ex.Message}");
                throw;
            }
        }

        public void SaveTelegramConfig()
        {
            try
            {
                var appConfigPath = Path.Combine(_configDirectory, AppConfigFileName);
                var appConfig = new AppConfig
                {
                    Services = _services,
                    Telegram = _telegramConfig
                };
                var json = JsonConvert.SerializeObject(appConfig, Formatting.Indented);
                WriteAllTextAtomic(appConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения конфигурации Telegram: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Атомарная запись текста в файл: пишем во временный файл, затем
        /// File.Move с заменой. Защищает от частичной записи, если процесс
        /// прерван посередине (или GUI пишет, а служба параллельно читает).
        /// </summary>
        private static void WriteAllTextAtomic(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Временный файл в той же папке (для File.Move по тому же тому)
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content, System.Text.Encoding.UTF8);

            // File.Move с overwrite=true атомарен на NTFS для замены внутри одного тома.
            // Если целевой файл открыт службой на чтение — будет IOException; вызывающая
            // сторона поймает и повторит попытку позже.
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        public void UpdateTelegramConfig(TelegramConfig config)
        {
            _telegramConfig = config;
            SaveTelegramConfig();
        }

        public void AddService(Service service)
        {
            _services.Add(service);
            SaveConfiguration();
        }

        public void UpdateService(int index, Service service)
        {
            if (index >= 0 && index < _services.Count)
            {
                _services[index] = service;
                SaveConfiguration();
            }
        }

        public void RemoveService(int index)
        {
            if (index >= 0 && index < _services.Count)
            {
                _services.RemoveAt(index);
                SaveConfiguration();
            }
        }

        // Метод для перезагрузки конфигурации (полезен для службы)
        public void ReloadConfiguration()
        {
            LoadConfiguration();
            LoadTelegramConfig();
        }
    }
}
