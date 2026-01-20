# Изменения - Добавление Windows Service

## Краткое описание изменений

Добавлен Windows Service для фоновой отправки Telegram-отчётов по расписанию, независимо от работы WPF-приложения.

## Структура добавленного Service-проекта

### Проекты

1. **BackupMonitor.Core** - Общая библиотека (.NET 8.0)
   - Модели: `Service`, `AppConfig`, `TelegramConfig`, `BackupReport`
   - Сервисы: `ConfigurationManager`, `BackupChecker`, `TelegramReportSender`
   - Не содержит зависимостей от WPF

2. **BackupMonitorService** - Windows Service (.NET 8.0 Windows)
   - Worker Service с автоматическим запуском при старте Windows
   - Использует `BackgroundService` + `PeriodicTimer` для планирования
   - Логирование через `ILogger` (Event Log + Console)

### Ключевые классы

#### 1. `BackupMonitorWorker` (BackupMonitorService/BackupMonitorWorker.cs)

Основной класс службы, наследуется от `BackgroundService`:
- Планирование отправки отчётов каждую минуту
- Проверка расписания из конфигурации
- Автоматическая перезагрузка конфигурации перед каждой проверкой
- Генерация отчёта за предыдущий день
- Обработка ошибок без падения службы

**Ключевые методы:**
- `ExecuteAsync()` - основной цикл службы
- `SendScheduledReportAsync()` - отправка запланированного отчёта
- `GenerateReportForPreviousDay()` - генерация отчёта за предыдущий день
- `IsTimeToSend()` - проверка, наступило ли время отправки

#### 2. `ConfigurationManager` (BackupMonitor.Core/Services/ConfigurationManager.cs)

Обновлённая версия без WPF зависимостей:
- Поддержка указания директории конфигурации
- Метод `ReloadConfiguration()` для перезагрузки настроек
- Работа с `services.json` и `appconfig.json`

#### 3. `TelegramReportSender` (BackupMonitor.Core/Services/TelegramReportSender.cs)

Перенесён в общую библиотеку без изменений логики:
- Отправка отчётов в Telegram через Bot API
- Форматирование отчётов (HTML)
- Фильтрация по режиму отчёта (FailOnly, OkOnly, Full)

#### 4. `WindowsServiceManager` (BackupMonitor/Services/WindowsServiceManager.cs)

Новый класс для управления Windows Service из WPF:
- `IsServiceInstalled()` - проверка установки службы
- `GetServiceStatus()` - получение статуса службы
- `StartService()` - запуск службы
- `StopService()` - остановка службы
- `InstallService()` - установка службы через `sc.exe`
- `UninstallService()` - удаление службы

## Функциональность службы

### Планирование

- Проверка расписания каждую минуту
- Поддержка нескольких времён отправки в день (формат `HH:mm`)
- Автоматический сброс списка отправленных отчётов при смене дня
- Защита от повторной отправки в одно и то же время

### Отчёт

- Формируется **за предыдущий день** (не за текущий)
- Фильтруется по настройкам:
  - `FailOnly` - только FAIL
  - `OkOnly` - только OK
  - `Full` - FAIL + OK

### Telegram

- Использует существующий bot token и chat id из конфигурации
- Отправка через Telegram Bot API
- Поддержка форматированного текста (HTML)

### Надёжность

- При ошибке отправки служба не падает
- Все ошибки логируются в Windows Event Log и Console
- Повторная попытка при следующем интервале проверки
- Обработка исключений на всех уровнях

## Взаимодействие с WPF

### WPF-приложение

- Используется только для настройки
- Сохраняет изменения конфигурации в `services.json` и `appconfig.json`
- Служба автоматически подхватывает изменения (перезагрузка конфигурации каждую минуту)

### Управление службой из WPF

Добавлен интерфейс в `MainWindow`:
- **Установить службу** - кнопка "Установить службу"
- **Запустить службу** - кнопка "Запустить"
- **Остановить службу** - кнопка "Остановить"
- **Проверить статус** - кнопка "Обновить статус"
- Отображение текущего статуса службы

## Изменения в существующем коде

### BackupMonitor (WPF)

- Обновлён для использования `BackupMonitor.Core`
- Добавлены адаптеры для обратной совместимости:
  - `BackupMonitor.Services.ConfigurationManager` - обёртка над Core
  - `BackupMonitor.Services.BackupChecker` - делегирует в Core
  - `BackupMonitor.Services.TelegramReportSender` - делегирует в Core
- `ReportScheduler` оставлен в WPF (использует `DispatcherTimer`)

### Модели

- Перенесены в `BackupMonitor.Core.Models`
- Старые файлы в `Models/` можно удалить (но оставлены для обратной совместимости)

## Конфигурация

Служба использует те же конфигурационные файлы, что и WPF:
- `services.json` - список сервисов для мониторинга
- `appconfig.json` - настройки Telegram (bot token, chat id, расписание, режим отчёта)

**Важно:** Оба приложения должны иметь доступ к одним и тем же конфигурационным файлам.

## Инструкция по установке службы

### Способ 1: Через WPF-приложение

1. Запустите WPF-приложение от имени администратора
2. Нажмите "Установить службу"
3. Укажите путь к `BackupMonitorService.exe` (если не найден автоматически)

### Способ 2: Через командную строку

```cmd
# От имени администратора
sc create BackupMonitorService binPath= "C:\path\to\BackupMonitorService.exe" start= auto DisplayName= "Backup Monitor Service"
sc start BackupMonitorService
```

### Управление службой

```cmd
# Запуск
sc start BackupMonitorService

# Остановка
sc stop BackupMonitorService

# Проверка статуса
sc query BackupMonitorService

# Удаление
sc delete BackupMonitorService
```

## Результат доработки

✅ После закрытия WPF-приложения служба продолжает работать  
✅ В заданное время Telegram-отчёт отправляется автоматически  
✅ Все настройки управляются через существующий интерфейс  
✅ Проект остаётся расширяемым  
✅ Служба не падает при ошибках  
✅ Все ошибки логируются  

## Зависимости

### BackupMonitor.Core
- Newtonsoft.Json (13.0.3)

### BackupMonitorService
- Microsoft.Extensions.Hosting (8.0.0)
- Microsoft.Extensions.Hosting.WindowsServices (8.0.0)
- Microsoft.Extensions.Logging.EventLog (8.0.0)
- Newtonsoft.Json (13.0.3)

### BackupMonitor (WPF)
- Использует BackupMonitor.Core через ProjectReference
- Использует System.ServiceProcess для управления службой

## Логирование

Служба логирует события в:
- **Windows Event Log** (источник: `BackupMonitorService`, лог: `Application`)
- **Console** (если запущена вручную)

Для просмотра логов:
1. Event Viewer (eventvwr.msc)
2. Windows Logs > Application
3. Источник: `BackupMonitorService`

## Примечания

- Служба перезагружает конфигурацию каждую минуту, поэтому изменения из WPF подхватываются автоматически
- Отчёты формируются за предыдущий день (не за текущий)
- Служба проверяет расписание каждую минуту для точного срабатывания
- Защита от повторной отправки: служба отслеживает уже отправленные отчёты за день
