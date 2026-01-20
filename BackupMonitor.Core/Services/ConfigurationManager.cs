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

        public void LoadConfiguration()
        {
            try
            {
                var configPath = Path.Combine(_configDirectory, ConfigFileName);
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    _services = JsonConvert.DeserializeObject<List<Service>>(json) ?? new List<Service>();
                }
                else
                {
                    _services = new List<Service>();
                }
            }
            catch (Exception ex)
            {
                // Логируем ошибку без использования MessageBox (так как это общая библиотека)
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки конфигурации: {ex.Message}");
                _services = new List<Service>();
            }
        }

        public void LoadTelegramConfig()
        {
            try
            {
                var appConfigPath = Path.Combine(_configDirectory, AppConfigFileName);
                if (File.Exists(appConfigPath))
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
