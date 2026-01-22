# Backup Monitor

Backup Monitor is a WPF (.NET 8) app plus a Windows Service and a Core library for monitoring backups in UNC/network folders. It supports multiple check modes, daily offsets, grouped services with child checks, and Telegram reporting.

---

# Backup Monitor (RU)

Backup Monitor — это WPF-приложение (.NET 8) + Windows Service + Core-библиотека для мониторинга бэкапов в сетевых (UNC) папках. Поддерживаются разные режимы проверки, смещение по дням, групповые сервисы с дочерними проверками и отчеты в Telegram.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022+

## Project structure

- `BackupMonitor/` - WPF GUI
- `BackupMonitor.Core/` - Core logic, models, checks, report formatting
- `BackupMonitorService/` - Windows Service
- `BackupMonitor.Tests/` - Unit tests (MSTest)
- `services.json` - service configuration (in app directory)
- `appconfig.json` - Telegram settings (in app directory, created by GUI)

## Структура проекта (RU)

- `BackupMonitor/` — WPF GUI
- `BackupMonitor.Core/` — Core-логика, модели, проверки, форматирование отчетов
- `BackupMonitorService/` — Windows Service
- `BackupMonitor.Tests/` — Unit-тесты (MSTest)
- `services.json` — конфигурация сервисов (в папке приложения)
- `appconfig.json` — настройки Telegram (в папке приложения, создаются GUI)

## Features

- Check backups by file name (regex) or by file timestamp.
- Expected day offset (today or yesterday).
- Minimum required files per day.
- Optional file mask filter (`*.bak`, `*.zip`).
- Group services with child results and aggregated status:
  - FAIL if any required child fails
  - WARNING if only optional children fail
  - OK if all required children are OK
  - ERROR on access/read errors
- Telegram report with summary and per-service status lines.
- Tree view in UI with groups and children.

## Возможности (RU)

- Проверка бэкапов по имени файла (regex) или по времени файла.
- Смещение ожидаемой даты (сегодня/вчера).
- Минимум файлов за день.
- Маска файлов (`*.bak`, `*.zip`).
- Групповые сервисы с дочерними результатами и агрегированным статусом:
  - FAIL если упал хотя бы один Required
  - WARNING если упали только Optional
  - OK если все Required OK
  - ERROR при ошибках доступа/чтения
- Telegram-отчет со сводкой и построчными статусами.
- Дерево сервисов в UI (группы + дети).

## Check modes

### NameDate
Extracts date from the file name using regex patterns.

### FileTime
Uses file `LastWriteTime` or `CreationTime`.

## Режимы проверки (RU)

### NameDate
Извлекает дату из имени файла по regex.

### FileTime
Берет дату из `LastWriteTime` или `CreationTime`.

## Configuration schema (services.json)

All fields are optional for backward compatibility unless noted.

```json
{
  "Name": "Service name",
  "Path": "\\\\server\\share\\backups",
  "Keywords": ["backup", "full"],
  "DatePatterns": ["(\\d{4}_\\d{2}_\\d{2})"],
  "ExpectedDayOffset": 0,
  "CheckMode": "NameDate",
  "FileTimeSource": "LastWriteTime",
  "MinFilesPerDay": 1,
  "FileMask": "*.bak",
  "Type": "Single",
  "Children": [],
  "Required": true,
  "ChildFolders": [],
  "UseChildFolderAsKeyword": true
}
```

### Notes

- `ExpectedDayOffset`: 0 = today, 1 = yesterday.
- `CheckMode`: `NameDate` or `FileTime`.
- `FileTimeSource`: `LastWriteTime` or `CreationTime`.
- `MinFilesPerDay`: OK if found files >= this value.
- `Type`: `Single` or `Group`.
- `Children`: explicit child services (advanced).
- `ChildFolders`: list of subfolders for bulk group setup.
- `UseChildFolderAsKeyword`: if `Keywords` empty, child name is used as keyword.

### Примечания (RU)

- `ExpectedDayOffset`: 0 = сегодня, 1 = вчера.
- `CheckMode`: `NameDate` или `FileTime`.
- `FileTimeSource`: `LastWriteTime` или `CreationTime`.
- `MinFilesPerDay`: OK если найдено файлов >= значения.
- `Type`: `Single` или `Group`.
- `Children`: явные дочерние сервисы (расширенный вариант).
- `ChildFolders`: список подпапок для групп.
- `UseChildFolderAsKeyword`: если `Keywords` пустые, имя подпапки используется как keyword.

## Group service (composite)

Use `Type = Group` and provide either:
- `Children` (explicit child services), or
- `ChildFolders` (list of subfolders under `Path`).

Example with ChildFolders:

```json
{
  "Name": "Conveer",
  "Type": "Group",
  "Path": "\\\\192.168.10.19\\ABS-Backup\\Conveer\\Backup",
  "ChildFolders": [
    "auth_db",
    "business_process_db",
    "client_db"
  ],
  "CheckMode": "NameDate",
  "DatePatterns": ["(\\d{4}_\\d{2}_\\d{2})"],
  "ExpectedDayOffset": 0,
  "MinFilesPerDay": 1,
  "UseChildFolderAsKeyword": true
}
```

## Групповой сервис (RU)

Используй `Type = Group` и задай либо:
- `Children` (явные дочерние сервисы), либо
- `ChildFolders` (список подпапок внутри `Path`).

## GUI usage

### Add a single service
1. Click **Add service**
2. Fill name, path, mode, patterns/time source, offsets, min files
3. Save

### Add a group with many subfolders
1. Click **Add group**
2. Set group name and base path
3. Click **Load subfolders from path** or paste list
4. Set check mode and other options
5. Save

The group will appear in the tree, with children listed under it.

## Использование GUI (RU)

### Добавить одиночный сервис
1. Нажми **Добавить сервис**
2. Укажи имя, путь, режим, regex/источник времени, смещения и минимум файлов
3. Сохрани

### Добавить группу с подпапками
1. Нажми **Добавить группу**
2. Укажи имя группы и базовый путь
3. Нажми **Загрузить подпапки из пути** или вставь список
4. Выбери режим проверки и параметры
5. Сохрани

Группа появится в дереве, дети будут вложены.

## Telegram report

Report includes:
- Header with date/time
- Summary counts (OK/WARNING/FAIL/ERROR)
- Per-service lines with emoji:
  - ✅ OK
  - ⚠️ WARNING
  - ❌ FAIL
  - 🔥 ERROR
- Child services printed as a quoted block inside their group

## Telegram-отчет (RU)

Содержит:
- Заголовок с датой/временем
- Сводку OK/WARNING/FAIL/ERROR
- По каждой строке сервиса:
  - ✅ OK
  - ⚠️ WARNING
  - ❌ FAIL
  - 🔥 ERROR
- Дочерние сервисы выводятся цитатой внутри группы

## Build and run

```bash
dotnet build
```

Run GUI from Visual Studio or `BackupMonitor` project.

## Сборка и запуск (RU)

```bash
dotnet build
```

GUI запускается из Visual Studio или проекта `BackupMonitor`.

## Tests

```bash
dotnet test
```

## Тесты (RU)

```bash
dotnet test
```

## Notes

- The app and service are Windows-only.
- The service uses the same `services.json` and `appconfig.json` configuration as the GUI.

## Примечания (RU)

- Приложение и служба — только для Windows.
- Служба использует те же `services.json` и `appconfig.json`, что и GUI.
