using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Windows;
using Microsoft.Win32;
using BackupMonitor.Core.Models;
using BackupMonitor.Services;
using BackupMonitor.Views;
using System.Threading.Tasks;

namespace BackupMonitor
{
    public partial class MainWindow : Window
    {
        private ConfigurationManager _configManager;
        private BackupChecker _backupChecker;
        private TelegramReportSender _telegramSender;
        private ReportScheduler _scheduler;
        private WindowsServiceManager _serviceManager;
        private ObservableCollection<ServiceViewModel> _services;

        public MainWindow()
        {
            InitializeComponent();
            
            // Используем директорию приложения для конфигурации
            var configDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configManager = new ConfigurationManager(configDirectory);
            _backupChecker = new BackupChecker();
            _telegramSender = new TelegramReportSender();
            _scheduler = new ReportScheduler(_configManager, _backupChecker, _telegramSender);
            _serviceManager = new WindowsServiceManager();
            _services = new ObservableCollection<ServiceViewModel>();
            
            LoadServices();
            RefreshServiceStatus();
            
            // Запускаем планировщик
            _scheduler.Start();
            
            // Предупреждаем о правах администратора при необходимости
            if (!ServiceInstallerHelper.IsRunningAsAdministrator())
            {
                // Не показываем сообщение сразу, чтобы не мешать - пользователь увидит при попытке установить службу
                // Просто оставляем информативное сообщение в статусе
                if (!_serviceManager.IsServiceInstalled())
                {
                    StatusText.Text = "Для установки службы требуются права администратора";
                }
            }
        }

        private void LoadServices()
        {
            _services.Clear();
            foreach (var service in _configManager.Services)
            {
                _services.Add(new ServiceViewModel
                {
                    Name = service.Name,
                    Path = service.Path,
                    Status = "-",
                    Details = ""
                });
            }
            ServicesDataGrid.ItemsSource = _services;
        }

        private async void BtnCheckToday_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Проверка бэкапов за сегодня...";
            BtnCheckToday.IsEnabled = false;
            BtnCheckPeriod.IsEnabled = false;
            
            try
            {
                var today = DateTime.Now.Date;
                
                await System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < _configManager.Services.Count; i++)
                    {
                        var service = _configManager.Services[i];
                        var result = _backupChecker.CheckBackupForDate(service, today);
                        
                        Dispatcher.Invoke(() =>
                        {
                            _services[i].Status = result.IsValid ? "OK" : "FAIL";
                            if (!string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                _services[i].Details = result.ErrorMessage;
                            }
                            else if (!result.IsValid)
                            {
                                _services[i].Details = $"Бэкап за {today:yyyy-MM-dd} не найден";
                            }
                            else
                            {
                                _services[i].Details = $"Бэкап за {today:yyyy-MM-dd} найден";
                            }
                        });
                    }
                });
                
                StatusText.Text = "Проверка завершена";
            }
            finally
            {
                BtnCheckToday.IsEnabled = true;
                BtnCheckPeriod.IsEnabled = true;
            }
        }

        private async void BtnCheckPeriod_Click(object sender, RoutedEventArgs e)
        {
            var periodWindow = new PeriodWindow();
            if (periodWindow.ShowDialog() == true)
            {
                var startDate = periodWindow.StartDate;
                var endDate = periodWindow.EndDate;

                // Выбор сервиса для проверки периода
                var selectWindow = new ServiceSelectWindow(_configManager.Services.Select(s => s.Name).ToList());
                if (selectWindow.ShowDialog() != true)
                    return;

                var selectedName = selectWindow.SelectedServiceName;
                if (string.IsNullOrEmpty(selectedName))
                {
                    return;
                }
                
                StatusText.Text = $"Проверка бэкапов за период {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}...";
                BtnCheckToday.IsEnabled = false;
                BtnCheckPeriod.IsEnabled = false;
                
                try
                {
                    if (selectedName == ServiceSelectWindow.AllServicesLabel)
                    {
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            for (int i = 0; i < _configManager.Services.Count; i++)
                            {
                                var service = _configManager.Services[i];
                                var result = _backupChecker.CheckBackupForPeriod(service, startDate, endDate);

                                Dispatcher.Invoke(() =>
                                {
                                    _services[i].Status = result.IsValid ? "OK" : "FAIL";
                                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                                    {
                                        _services[i].Details = result.ErrorMessage;
                                    }
                                    else if (result.MissingDates.Count > 0)
                                    {
                                        // Показываем все даты - пользователь сможет прокрутить вправо
                                        var datesList = result.MissingDates.Select(d => d.ToString("yyyy-MM-dd")).ToList();
                                        _services[i].Details = $"Отсутствуют бэкапы: {result.MissingDates.Count} дн. ({string.Join(", ", datesList)})";
                                    }
                                    else
                                    {
                                        _services[i].Details = "Все бэкапы найдены";
                                    }
                                });
                            }
                        });
                    }
                    else
                    {
                        var serviceIndex = -1;
                        for (int i = 0; i < _configManager.Services.Count; i++)
                        {
                            if (_configManager.Services[i].Name == selectedName)
                            {
                                serviceIndex = i;
                                break;
                            }
                        }
                        
                        if (serviceIndex < 0)
                        {
                            MessageBox.Show("Сервис не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            StatusText.Text = "Ошибка: сервис не найден";
                            BtnCheckToday.IsEnabled = true;
                            BtnCheckPeriod.IsEnabled = true;
                            return;
                        }

                        var result = await System.Threading.Tasks.Task.Run(() =>
                            _backupChecker.CheckBackupForPeriod(_configManager.Services[serviceIndex], startDate, endDate));

                        // Обновляем строку сервиса
                        _services[serviceIndex].Status = result.IsValid ? "OK" : "FAIL";
                        if (!string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            _services[serviceIndex].Details = result.ErrorMessage;
                        }
                        else if (result.MissingDates.Count > 0)
                        {
                            _services[serviceIndex].Details = $"Отсутствуют бэкапы: {result.MissingDates.Count} дн.";
                        }
                        else
                        {
                            _services[serviceIndex].Details = "Все бэкапы найдены";
                        }

                        // Показываем отдельное окно с результатами периода
                        var resultWindow = new PeriodResultWindow(selectedName, startDate, endDate, result);
                        resultWindow.Owner = this;
                        resultWindow.ShowDialog();
                    }
                    
                    StatusText.Text = "Проверка завершена";
                }
                finally
                {
                    BtnCheckToday.IsEnabled = true;
                    BtnCheckPeriod.IsEnabled = true;
                }
            }
        }

        private void BtnAddService_Click(object sender, RoutedEventArgs e)
        {
            var serviceWindow = new ServiceWindow();
            if (serviceWindow.ShowDialog() == true)
            {
                var service = serviceWindow.Service;
                _configManager.AddService(service);
                LoadServices();
                StatusText.Text = "Сервис добавлен";
            }
        }

        private void BtnEditService_Click(object sender, RoutedEventArgs e)
        {
            if (ServicesDataGrid.SelectedIndex < 0)
            {
                MessageBox.Show("Выберите сервис для редактирования", "Внимание", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var index = ServicesDataGrid.SelectedIndex;
            var service = _configManager.Services[index];
            var serviceWindow = new ServiceWindow(service);
            
            if (serviceWindow.ShowDialog() == true)
            {
                var updatedService = serviceWindow.Service;
                _configManager.UpdateService(index, updatedService);
                LoadServices();
                StatusText.Text = "Сервис обновлён";
            }
        }

        private void BtnDeleteService_Click(object sender, RoutedEventArgs e)
        {
            if (ServicesDataGrid.SelectedIndex < 0)
            {
                MessageBox.Show("Выберите сервис для удаления", "Внимание", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("Вы уверены, что хотите удалить выбранный сервис?", 
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var index = ServicesDataGrid.SelectedIndex;
                _configManager.RemoveService(index);
                LoadServices();
                StatusText.Text = "Сервис удалён";
            }
        }

        private void ServicesDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            BtnEditService.IsEnabled = ServicesDataGrid.SelectedIndex >= 0;
            BtnDeleteService.IsEnabled = ServicesDataGrid.SelectedIndex >= 0;
        }

        private void BtnTelegramSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new TelegramSettingsWindow(_configManager.TelegramConfig);
            if (settingsWindow.ShowDialog() == true)
            {
                _configManager.UpdateTelegramConfig(settingsWindow.Config);
                StatusText.Text = "Настройки Telegram сохранены";
            }
        }

        private async void BtnSendReport_Click(object sender, RoutedEventArgs e)
        {
            var config = _configManager.TelegramConfig;
            if (!config.Enabled)
            {
                MessageBox.Show("Telegram уведомления отключены. Включите их в настройках Telegram.", 
                    "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(config.BotToken) || string.IsNullOrWhiteSpace(config.ChatId))
            {
                MessageBox.Show("Не настроены Bot Token или Chat ID. Проверьте настройки Telegram.", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnSendReport.IsEnabled = false;
            StatusText.Text = "Отправка отчёта...";

            try
            {
                var result = await _scheduler.SendReportNowAsync();
                if (result.Status == ReportSendStatus.Sent)
                {
                    StatusText.Text = "Отчёт успешно отправлен в Telegram";
                    MessageBox.Show("Отчёт успешно отправлен в Telegram!",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (result.Status == ReportSendStatus.Skipped)
                {
                    var infoMessage = result.Message ?? "Отчёт не был отправлен.";
                    StatusText.Text = infoMessage;
                    MessageBox.Show(infoMessage,
                        "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusText.Text = "Ошибка отправки отчёта";
                    MessageBox.Show(result.Message ?? "Не удалось отправить отчёт. Проверьте настройки Telegram (Bot Token, Chat ID) и убедитесь, что бот добавлен в группу.",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                var errorMsg = ex.Message;
                if (errorMsg.Contains("Telegram API"))
                {
                    errorMsg = errorMsg.Replace("Telegram API: ", "");
                }
                MessageBox.Show($"Ошибка отправки отчёта:\n\n{errorMsg}\n\nПроверьте:\n- Bot Token правильный\n- Chat ID правильный\n- Бот добавлен в группу\n- Бот имеет права на отправку сообщений", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSendReport.IsEnabled = true;
            }
        }

        private async void BtnInstallService_Click(object sender, RoutedEventArgs e)
        {
            // Блокируем кнопку на время установки
            BtnInstallService.IsEnabled = false;
            StatusText.Text = "Начало установки службы...";

            try
            {
                if (_serviceManager.IsServiceInstalled())
                {
                    var reinstallResult = MessageBox.Show(
                        "Служба уже установлена. Переустановить службу?",
                        "Переустановка",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (reinstallResult != MessageBoxResult.Yes)
                    {
                        BtnInstallService.IsEnabled = true;
                        StatusText.Text = "Переустановка отменена";
                        return;
                    }
                }

                // Проверяем права администратора сразу
                if (!ServiceInstallerHelper.IsRunningAsAdministrator())
                {
                    var result = MessageBox.Show(
                        "Для установки службы требуются права администратора.\n\n" +
                        "Приложение будет перезапущено от имени администратора.\n\n" +
                        "Продолжить?",
                        "Требуются права администратора", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Перезапускаем приложение от имени администратора
                        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exePath,
                            Verb = "runas",
                            UseShellExecute = true
                        };

                        try
                        {
                            System.Diagnostics.Process.Start(processInfo);
                            Application.Current.Shutdown();
                            return;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"Не удалось перезапустить приложение от имени администратора:\n\n{ex.Message}\n\n" +
                                "Пожалуйста, закройте приложение и запустите его вручную от имени администратора.",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            BtnInstallService.IsEnabled = true;
                            return;
                        }
                    }
                    else
                    {
                        BtnInstallService.IsEnabled = true;
                        StatusText.Text = "Установка отменена";
                        return;
                    }
                }

                // Получаем директорию конфигурации
                var configDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

                // Создаем прогресс для отображения статуса
                var progressMessages = new System.Collections.Generic.List<string>();
                IProgress<string> progress = new Progress<string>(msg =>
                {
                    progressMessages.Add(msg);
                    StatusText.Text = msg;
                    Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                });

                // Выполняем автоматическую установку
                var installResult = await ServiceInstallerHelper.InstallServiceOneClickAsync(
                    configDirectory, 
                    _serviceManager, 
                    progress);

                // Даем время на регистрацию службы в системе
                await Task.Delay(1500);

                // Обновляем статус службы несколько раз для надежности
                RefreshServiceStatus();
                await Task.Delay(500);
                RefreshServiceStatus();

                if (installResult.Success)
                {
                    // Проверяем реальный статус службы после установки
                    var isActuallyInstalled = _serviceManager.IsServiceInstalled();
                    var actualStatus = _serviceManager.GetServiceStatus();

                    if (isActuallyInstalled)
                    {
                        if (actualStatus == System.ServiceProcess.ServiceControllerStatus.Running)
                        {
                            MessageBox.Show(
                                "Служба успешно установлена и запущена!",
                                "Успех", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Information);
                            StatusText.Text = "Служба установлена и запущена";
                        }
                        else if (actualStatus.HasValue)
                        {
                            var statusText = _serviceManager.GetServiceStatusText();
                            MessageBox.Show(
                                $"Служба успешно установлена, но не запущена.\n\n" +
                                $"Текущий статус: {statusText}\n\n" +
                                $"Возможные причины:\n" +
                                $"1. Ошибка в конфигурационных файлах (services.json, appconfig.json)\n" +
                                $"2. Отсутствуют необходимые файлы или зависимости\n" +
                                $"3. Проблемы с правами доступа\n\n" +
                                $"Попробуйте запустить службу вручную или проверьте логи Event Viewer.",
                                "Служба установлена", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Warning);
                            StatusText.Text = $"Служба установлена: {statusText}";
                        }
                        else
                        {
                            MessageBox.Show(
                                "Служба установлена, но не удалось определить её статус.\n\n" +
                                "Попробуйте обновить статус или проверить службу вручную.",
                                "Служба установлена", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Warning);
                            StatusText.Text = "Служба установлена (статус неизвестен)";
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "Процесс установки завершился, но служба не найдена в системе.\n\n" +
                            "Проверьте:\n" +
                            "1. Логи установки выше\n" +
                            "2. Запущено ли приложение от имени администратора\n" +
                            "3. Логи Event Viewer",
                            "Проблема с установкой", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Warning);
                        StatusText.Text = "Проблема с установкой службы";
                    }
                }
                else
                {
                    var errorDetails = string.Join("\n", progressMessages);
                    MessageBox.Show(
                        $"{installResult.ErrorMessage}\n\nДетали установки:\n{errorDetails}\n\n" +
                        "Проверьте:\n" +
                        "1. Запущено ли приложение от имени администратора\n" +
                        "2. Существует ли файл BackupMonitorService.exe\n" +
                        "3. Правильность путей к файлам\n" +
                        "4. Логи Event Viewer",
                        "Ошибка установки", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                    StatusText.Text = "Ошибка установки службы";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Критическая ошибка при установке службы:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "Ошибка", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                BtnInstallService.IsEnabled = true;
            }
        }

        private void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_serviceManager.IsServiceInstalled())
                {
                    MessageBox.Show("Служба не установлена. Сначала установите службу.", "Внимание", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    RefreshServiceStatus();
                    return;
                }

                if (!ServiceInstallerHelper.IsServiceExecutablePresent())
                {
                    MessageBox.Show("Файлы службы не найдены. Переустановите службу.", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    RefreshServiceStatus();
                    return;
                }

                if (!ServiceInstallerHelper.IsRunningAsAdministrator())
                {
                    MessageBox.Show("Для запуска службы нужны права администратора. Запустите приложение от имени администратора.", "Внимание", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    RefreshServiceStatus();
                    return;
                }

                // Проверяем текущий статус службы
                var currentStatus = _serviceManager.GetServiceStatus();
                if (currentStatus == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    MessageBox.Show("Служба уже запущена.", "Информация", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshServiceStatus();
                    return;
                }

                BtnStartService.IsEnabled = false;
                StatusText.Text = "Запуск службы...";

                if (_serviceManager.StartService())
                {
                    // Даем время на обновление статуса
                    System.Threading.Thread.Sleep(500);
                    MessageBox.Show("Служба успешно запущена!", "Успех", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusText.Text = "Служба запущена";
                }
                else
                {
                    // Получаем более детальную информацию об ошибке
                    var status = _serviceManager.GetServiceStatus();
                    string errorMessage = "Не удалось запустить службу.";
                    
                    if (status.HasValue)
                    {
                        if (status.Value == System.ServiceProcess.ServiceControllerStatus.Stopped)
                        {
                            errorMessage = "Служба остановлена. Возможные причины:\n" +
                                         "- Недостаточно прав для запуска службы\n" +
                                         "- Отсутствуют необходимые зависимости\n" +
                                         "- Проверьте логи Event Viewer для деталей";
                        }
                        else if (status.Value == System.ServiceProcess.ServiceControllerStatus.Paused)
                        {
                            errorMessage = "Служба приостановлена. Сначала возобновите её работу.";
                        }
                    }
                    else
                    {
                        errorMessage = "Не удалось запустить службу.\n\n" +
                                     "Возможные причины:\n" +
                                     "- Служба не установлена или была удалена\n" +
                                     "- Ошибка в конфигурационных файлах\n" +
                                     "- Недостаточно прав для запуска службы\n" +
                                     "- Проверьте логи Event Viewer для деталей";
                    }

                    MessageBox.Show(errorMessage, "Ошибка запуска службы", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Ошибка запуска службы";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска службы: {ex.Message}\n\nПроверьте:\n" +
                              "- Установлена ли служба\n" +
                              "- Наличие файлов services.json и appconfig.json в директории службы\n" +
                              "- Логи Event Viewer", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                RefreshServiceStatus();
            }
        }

        private void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_serviceManager.IsServiceInstalled())
                {
                    MessageBox.Show("Служба не установлена.", "Внимание", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    RefreshServiceStatus();
                    return;
                }

                if (!ServiceInstallerHelper.IsRunningAsAdministrator())
                {
                    MessageBox.Show("Для остановки службы нужны права администратора. Запустите приложение от имени администратора.", "Внимание", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    RefreshServiceStatus();
                    return;
                }

                // Проверяем текущий статус службы
                var currentStatus = _serviceManager.GetServiceStatus();
                if (currentStatus == System.ServiceProcess.ServiceControllerStatus.Stopped)
                {
                    MessageBox.Show("Служба уже остановлена.", "Информация", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshServiceStatus();
                    return;
                }

                var result = MessageBox.Show("Вы уверены, что хотите остановить службу?", 
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    BtnStopService.IsEnabled = false;
                    StatusText.Text = "Остановка службы...";

                    if (_serviceManager.StopService())
                    {
                        // Даем время на обновление статуса
                        System.Threading.Thread.Sleep(500);
                        MessageBox.Show("Служба успешно остановлена!", "Успех", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        StatusText.Text = "Служба остановлена";
                    }
                    else
                    {
                        var status = _serviceManager.GetServiceStatus();
                        string errorMessage = "Не удалось остановить службу.";
                        
                        if (status.HasValue)
                        {
                            if (status.Value == System.ServiceProcess.ServiceControllerStatus.StopPending)
                            {
                                errorMessage = "Служба находится в процессе остановки. Подождите немного.";
                            }
                            else
                            {
                                errorMessage = $"Не удалось остановить службу. Текущий статус: {_serviceManager.GetServiceStatusText()}";
                            }
                        }

                        MessageBox.Show(errorMessage, "Ошибка остановки службы", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusText.Text = "Ошибка остановки службы";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка остановки службы: {ex.Message}\n\nПроверьте:\n" +
                              "- Установлена ли служба\n" +
                              "- Логи Event Viewer", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Ошибка: {ex.Message}";
            }
            finally
            {
                RefreshServiceStatus();
            }
        }

        private void BtnRefreshServiceStatus_Click(object sender, RoutedEventArgs e)
        {
            RefreshServiceStatus();
        }

        private void RefreshServiceStatus()
        {
            bool isInstalled = false;
            ServiceControllerStatus? serviceStatus = null;
            bool serviceExePresent = ServiceInstallerHelper.IsServiceExecutablePresent();

            try
            {
                // Проверяем, установлена ли служба
                isInstalled = _serviceManager.IsServiceInstalled();
            }
            catch (Exception)
            {
                // При ошибке проверки установки считаем, что служба не установлена
                // Это может произойти, если нет прав или службы действительно нет
                isInstalled = false;
            }

            // Получаем статус службы только если она установлена
            if (isInstalled)
            {
                try
                {
                    if (serviceExePresent)
                    {
                        serviceStatus = _serviceManager.GetServiceStatus();
                        var statusText = _serviceManager.GetServiceStatusText();
                        ServiceStatusText.Text = statusText;
                    }
                    else
                    {
                        ServiceStatusText.Text = "Установлена (файлы отсутствуют)";
                        serviceStatus = null;
                    }
                }
                catch (Exception)
                {
                    // При ошибке получения статуса установленной службы
                    // Проверяем еще раз, может служба была удалена
                    try
                    {
                        isInstalled = _serviceManager.IsServiceInstalled();
                        if (!isInstalled)
                        {
                            ServiceStatusText.Text = "Не установлена";
                        }
                        else
                        {
                            ServiceStatusText.Text = "Ошибка получения статуса";
                        }
                    }
                    catch
                    {
                        ServiceStatusText.Text = "Ошибка проверки";
                        isInstalled = false;
                    }
                    serviceStatus = null;
                }
            }
            else
            {
                // Если служба не установлена, показываем стандартный текст
                ServiceStatusText.Text = "Не установлена";
            }

            // Обновляем состояние кнопок
            // Кнопка установки доступна всегда, но при наличии службы работает как переустановка
            BtnInstallService.IsEnabled = true;
            BtnInstallService.Content = isInstalled ? "Переустановить службу" : "Установить службу";
            
            // Кнопки управления службой активны только если служба установлена
            if (isInstalled)
            {
                if (!serviceExePresent)
                {
                    BtnStartService.IsEnabled = false;
                    BtnStopService.IsEnabled = false;
                    return;
                }

                // Проверяем статус только если он успешно получен
                if (serviceStatus.HasValue)
                {
                    BtnStartService.IsEnabled = serviceStatus.Value != System.ServiceProcess.ServiceControllerStatus.Running;
                    BtnStopService.IsEnabled = serviceStatus.Value == System.ServiceProcess.ServiceControllerStatus.Running;
                }
                else
                {
                    // Если статус не определен, но служба установлена, разрешаем попытку запуска
                    // (на случай проблем с получением статуса)
                    BtnStartService.IsEnabled = true;
                    BtnStopService.IsEnabled = false;
                }
            }
            else
            {
                // Если служба не установлена, отключаем кнопки управления
                BtnStartService.IsEnabled = false;
                BtnStopService.IsEnabled = false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _scheduler?.Stop();
            _telegramSender?.Dispose();
            base.OnClosed(e);
        }
    }

    public class ServiceViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _path = string.Empty;
        private string _status = "-";
        private string _details = string.Empty;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(nameof(Path)); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public string Details
        {
            get => _details;
            set { _details = value; OnPropertyChanged(nameof(Details)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
