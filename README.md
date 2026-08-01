# Backup Monitor

WPF-приложение (.NET 8) + Windows Service + Core-библиотека для мониторинга бэкапов в сетевых (UNC) папках. Проверяет бэкапы по расписанию, отправляет отчёты в Telegram и принимает команды от бота.

## Требования

- Windows 10/11
- .NET 8 SDK (для сборки)
- Visual Studio 2022+ (опционально)

## Структура проекта

| Проект | Назначение |
|---|---|
| `BackupMonitor/` | WPF GUI — настройка сервисов, проверка вручную, управление службой |
| `BackupMonitor.Core/` | Core-логика: модели, проверки, форматирование отчётов, Telegram-бот, логирование |
| `BackupMonitorService/` | Windows Service — фоновая проверка по расписанию + рассылка отчётов + бот |
| `BackupMonitor.Tests/` | Unit-тесты (MSTest) |

**Файлы конфигурации** (в папке приложения):
- `services.json` — список сервисов для мониторинга
- `appconfig.json` — настройки Telegram (bot token, chat id, расписание, режим отчёта)

---

## Возможности

- **Режимы проверки**: по имени файла (regex) или по timestamp (`LastWriteTime` / `CreationTime`)
- **Смещение даты**: сегодня / вчера (ExpectedDayOffset)
- **Минимум файлов за день**, маска файлов (`*.bak`, `*.zip`)
- **Минимальный размер файла** (`MinFileSizeBytes`): файлы меньше порога помечаются как ERROR (подозрение на пустой/битый бэкап). `0` = проверка отключена
- **Групповые сервисы** с дочерними проверками и агрегированным статусом:
  - FAIL — упал хотя бы один Required
  - WARNING — упали только Optional
  - OK — все Required OK
  - ERROR — ошибка доступа / чтения / размер не соответствует указанному
- **Telegram-отчёты** по расписанию с HTML-форматированием
- **Telegram-бот** — запрос отчётов по команде (`/report`, `/check`, `/services`)
- **Управление Windows-службой** из GUI (установка, запуск, остановка)
- **Tray-иконка** с быстрым доступом и фоновым мониторингом

---

## Режимы проверки

### NameDate
Извлекает дату из имени файла по regex-паттернам из поля `DatePatterns`.

### FileTime
Берёт дату из `LastWriteTime` или `CreationTime` (настраивается через `FileTimeSource`).

---

## Конфигурация `services.json`

Все поля необязательны для обратной совместимости.

```json
{
  "Name": "Имя сервиса",
  "Path": "\\\\server\\share\\backups",
  "Keywords": ["backup", "full"],
  "DatePatterns": ["(\\d{4}_\\d{2}_\\d{2})"],
  "ExpectedDayOffset": 0,
  "CheckMode": "NameDate",
  "FileTimeSource": "LastWriteTime",
  "MinFilesPerDay": 1,
  "MinFileSizeBytes": 0,
  "FileMask": "*.bak",
  "Type": "Single",
  "Children": [],
  "Required": true,
  "ChildFolders": [],
  "UseChildFolderAsKeyword": true
}
```

| Поле | Описание |
|---|---|
| `Name` | Имя сервиса |
| `Path` | UNC-путь к папке с бэкапами |
| `Keywords` | Ключевые слова для фильтрации файлов |
| `DatePatterns` | Regex-паттерны для извлечения даты из имени файла |
| `ExpectedDayOffset` | `0` = сегодня, `1` = вчера |
| `CheckMode` | `NameDate` или `FileTime` |
| `FileTimeSource` | `LastWriteTime` или `CreationTime` |
| `MinFilesPerDay` | Минимум файлов за день для статуса OK |
| `MinFileSizeBytes` | Минимальный размер файла (байты). `0` = не проверять |
| `FileMask` | Маска файла (например `*.bak`) |
| `Type` | `Single` или `Group` |
| `Children` | Явные дочерние сервисы (расширенный вариант) |
| `ChildFolders` | Список подпапок для автогенерации дочерних сервисов |
| `UseChildFolderAsKeyword` | Если `true` и `Keywords` пустые, имя подпапки используется как keyword |
| `Required` | `true` = Required (FAIL блокирует группу), `false` = Optional (только WARNING) |

### Пример группы с подпапками

```json
{
  "Name": "Conveer",
  "Type": "Group",
  "Path": "\\\\192.168.0.1\\ABS-Backup\\Backup",
  "ChildFolders": ["auth_db", "business_process_db", "client_db"],
  "CheckMode": "NameDate",
  "DatePatterns": ["(\\d{4}_\\d{2}_\\d{2})"],
  "ExpectedDayOffset": 0,
  "MinFilesPerDay": 1,
  "UseChildFolderAsKeyword": true
}
```

---

## Telegram-бот: команды

Бот запускается в составе Windows Service. Доступ по белому списку `AllowedChatIds` в `appconfig.json`.

| Команда | Описание |
|---|---|
| `/start`, `/help` | Список доступных команд |
| `/report` | Краткий отчёт за сегодня |
| `/report today` | Подробный отчёт за сегодня |
| `/report ok` | Только успешные бэкапы |
| `/report fail` | Только неуспешные (FAIL + ERROR) |
| `/report month` | Сводка за месяц (OK/FAIL по дням) |
| `/report period YYYY-MM-DD YYYY-MM-DD` | Произвольный период |
| `/services` | Список настроенных сервисов |
| `/check <имя>` | Проверить конкретный сервис сейчас |

Длинные сообщения автоматически разбиваются на чанки ≤ 4000 символов.

---

## Telegram-отчёт (по расписанию)

Служба отправляет отчёт автоматически в заданное время (настраивается в GUI). Формат:

- Заголовок с датой и временем
- Сводка: `✅ OK: N | ⚠️ WARNING: N | ❌ FAIL: N | 🔥 ERROR: N`
- По каждому сервису: статус с эмодзи и описание ошибки (если есть)
- Группы отображаются заголовком `📁 Группа «name»`, дочерние сервисы — в цитате (`<blockquote>`)
- Статусы: ✅ OK, ⚠️ WARNING, ❌ FAIL, 🔥 ERROR

---

## Управление Windows-службой

### Из GUI

1. Запустить приложение **от имени администратора**
2. Кнопки в панели управления службой: **Установить**, **Запустить**, **Остановить**, **Обновить статус**

### Из командной строки

```cmd
:: От имени администратора
sc create BackupMonitorService binPath= "C:\path\to\BackupMonitorService.exe" start= auto
sc start BackupMonitorService
sc stop BackupMonitorService
sc query BackupMonitorService
sc delete BackupMonitorService
```

### Самостоятельная публикация (для серверов без .NET SDK)

```cmd
dotnet publish BackupMonitorService -c Release -r win-x64 --self-contained -o publish
```

Готовые файлы из `publish/` копируются на сервер — .NET SDK на сервере не нужен.

---

## Логирование

Логи пишутся в `service.log` в папке приложения (общий для worker и бота через `FileLogger`).

- Автоматическая ротация при достижении 5 МБ
- Бот помечает свои записи префиксом `[BOT]`
- При ошибках Telegram-бота используется экспоненциальный backoff: 2с → 4с → 8с → 16с → 32с → 60с макс.

---

## GUI

### Добавить одиночный сервис
1. Нажать **Добавить сервис**
2. Указать имя, путь, режим проверки, regex/источник времени, смещения и минимум файлов
3. Сохранить

### Добавить/редактировать группу с подпапками
1. Нажать **Добавить группу** (или **Редактировать** для существующей)
2. Указать имя группы и базовый путь
3. Нажать **Загрузить подпапки из пути** или вставить список вручную
4. Выбрать режим проверки и параметры
5. Сохранить

Группа появляется в дереве, дочерние сервисы вложены под ней. Редактирование доступно через контекстное меню или двойной клик.

---

## Сборка и запуск

```bash
dotnet build
dotnet test
```

GUI запускается из Visual Studio или проекта `BackupMonitor`. Служба — через `BackupMonitorService.exe` или `sc start`.

---

## Жизненный цикл проекта

- Приложение и служба — только для Windows
- GUI и служба используют одни и те же `services.json` и `appconfig.json`
- Служба перезагружает конфигурацию перед каждой проверкой — изменения из GUI подхватываются автоматически
- Отчёты формируются за **предыдущий день** (настраивается)
- Защита от повторной отправки: отслеживание через `.sentstate.json`
- Heartbeat: `.heartbeat` файл обновляется каждый цикл для мониторинга жизнеспособности службы
