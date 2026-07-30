using System;
using System.IO;
using System.Linq;
using System.Windows;
using BackupMonitor.Core.Models;
using BackupMonitor.Services;
using Newtonsoft.Json;

namespace BackupMonitor
{
    public partial class App : Application
    {
        public const string ThemeLight = "Light";
        public const string ThemeDark = "Dark";

        private const string AppConfigFileName = "appconfig.json";
        private const string SyncArg = "--sync-config";

        private TrayIconManager? _trayIcon;
        private MainWindow? _mainWindow;

        /// <summary>
        /// Текущая тема (Light/Dark). По умолчанию восстанавливается из appconfig.json.
        /// </summary>
        public string CurrentTheme { get; private set; } = ThemeLight;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Режим "elevation helper": запущены с --sync-config после UAC-запроса.
            // Копируем конфиг в ProgramData и сразу выходим без UI.
            if (e.Args != null && e.Args.Any(a => string.Equals(a, SyncArg, StringComparison.OrdinalIgnoreCase)))
            {
                RunSyncConfigMode();
                Shutdown();
                return;
            }

            // Применяем сохранённую тему до показа главного окна
            var saved = ReadSavedTheme();
            ApplyTheme(saved);

            // Создаём и показываем главное окно вручную (StartupUri убран из App.xaml)
            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;
            _mainWindow.Show();

            // Инициализируем системный трей
            _trayIcon = new TrayIconManager(
                showMainWindow: ShowMainWindow,
                runCheck: () => _mainWindow.Dispatcher.Invoke(_mainWindow.RunCheckTodayFromTray));
        }

        /// <summary>
        /// Запуск elevated-режима: синхронизация конфигурации в ProgramData без UI.
        /// </summary>
        private void RunSyncConfigMode()
        {
            try
            {
                var guiConfigDir = AppDomain.CurrentDomain.BaseDirectory;
                ServiceInstallerHelper.SyncConfigNow(guiConfigDir);
            }
            catch
            {
                // В silent-режиме игнорируем — родительский процесс проверит результат отдельно
            }
        }

        /// <summary>
        /// Показать главное окно (из трея или при повторном запуске).
        /// </summary>
        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _mainWindow.Topmost = true;
                _mainWindow.Topmost = false;
            });
        }

        /// <summary>
        /// Переключает тему приложения и сохраняет выбор в appconfig.json.
        /// </summary>
        public void ToggleTheme()
        {
            var newTheme = CurrentTheme == ThemeLight ? ThemeDark : ThemeLight;
            ApplyTheme(newTheme);
            SaveTheme(CurrentTheme);
        }

        /// <summary>
        /// Применяет указанную тему, заменяя палитру цветов в ресурсах приложения.
        /// ВАЖНО: заменяем ТОЛЬКО словарь верхнего уровня в App.Resources.
        /// Вложенный Colors.xaml внутри Controls.xaml не трогаем — каждый раз
        /// создаём НОВЫЙ экземпляр ResourceDictionary, иначе один и тот же
        /// объект окажется в двух местах одновременно и WPF зациклится
        /// (StackOverflowException в ResourceDictionary.GetValue).
        /// Lookup DynamicResource сам найдёт ключи в App.Resources.
        /// </summary>
        public void ApplyTheme(string theme)
        {
            CurrentTheme = theme == ThemeDark ? ThemeDark : ThemeLight;

            var colorSource = CurrentTheme == ThemeDark
                ? new Uri("pack://application:,,,/Themes/Colors.Dark.xaml", UriKind.Absolute)
                : new Uri("pack://application:,,,/Themes/Colors.xaml", UriKind.Absolute);

            try
            {
                var newColors = new ResourceDictionary { Source = colorSource };
                ReplaceColorDictionary(Resources.MergedDictionaries, newColors);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyTheme error: {ex.Message}");
            }
        }

        /// <summary>
        /// Находит и заменяет Colors-словарь в коллекции.
        /// </summary>
        private static void ReplaceColorDictionary(System.Collections.Generic.IList<ResourceDictionary> collection, ResourceDictionary newColors)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                var d = collection[i];
                var src = d.Source?.OriginalString ?? string.Empty;
                if (src.EndsWith("/Colors.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.EndsWith("/Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    collection[i] = newColors;
                    return;
                }
            }

            // Не нашли — вставляем в начало
            collection.Insert(0, newColors);
        }

        private string ReadSavedTheme()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfigFileName);
                if (!File.Exists(path)) return ThemeLight;
                var json = File.ReadAllText(path);
                var doc = Newtonsoft.Json.Linq.JObject.Parse(json);
                var t = doc["Telegram"]?["Theme"]?.ToString();
                return string.Equals(t, ThemeDark, StringComparison.OrdinalIgnoreCase) ? ThemeDark : ThemeLight;
            }
            catch
            {
                return ThemeLight;
            }
        }

        private void SaveTheme(string theme)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConfigFileName);
                Newtonsoft.Json.Linq.JObject root;
                if (File.Exists(path))
                {
                    root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                }
                else
                {
                    root = new Newtonsoft.Json.Linq.JObject();
                }

                var telegram = root["Telegram"] as Newtonsoft.Json.Linq.JObject;
                if (telegram == null)
                {
                    telegram = new Newtonsoft.Json.Linq.JObject();
                    root["Telegram"] = telegram;
                }
                telegram["Theme"] = theme;

                File.WriteAllText(path, root.ToString(Formatting.Indented));
            }
            catch
            {
                // тема — некритичная настройка
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // MainWindow.OnClosed НЕ вызывается при OnClosing(e.Cancel=true),
            // поэтому освобождаем ресурсы здесь.
            _mainWindow?.CleanupResources();
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
