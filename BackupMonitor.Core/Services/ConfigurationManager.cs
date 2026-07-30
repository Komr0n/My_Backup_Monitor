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

                        // Исправляем Chat ID при загрузке, если нужно
                        if (!string.IsNullOrEmpty(_telegramConfig.ChatId))
                        {
                            var chatId = _telegramConfig.ChatId.Trim();
                            // Если это похоже на ID группы без минуса - добавляем
                            if (chatId.StartsWith("100") && chatId.Length > 10 && !chatId.StartsWith("-"))
                            {
                                _telegramConfig.ChatId = "-" + chatId;
                                // Сохраняем исправленную конфигурацию
                                SaveTelegramConfig();
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
                File.WriteAllText(configPath, json);
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
                File.WriteAllText(appConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения конфигурации Telegram: {ex.Message}");
                throw;
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
