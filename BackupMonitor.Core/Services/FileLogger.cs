using System;
using System.IO;

namespace BackupMonitor.Core.Services
{
    /// <summary>
    /// Единый файловый логгер с ротацией и блокировкой.
    /// Используется и BackupMonitorWorker, и TelegramCommandBot — один и тот же
    /// экземпляр через DI, с общим lock, чтобы два писателя не конфликтовали
    /// при одновременной записи в service.log.
    /// Ротация: при превышении MaxSizeBytes текущий лог переименовывается
    /// в .log.1 (предыдущий .1 удаляется), начинается новый файл.
    /// </summary>
    public class FileLogger
    {
        private const long MaxSizeBytes = 5 * 1024 * 1024; // 5 МБ до ротации

        private readonly string _filePath;
        private readonly object _lock = new object();

        /// <summary>
        /// Создаёт логгер, пишущий в указанный каталог и файл.
        /// </summary>
        /// <param name="directory">Полный путь к каталогу логов (например, %ProgramData%\BackupMonitorService).</param>
        /// <param name="fileName">Имя файла лога (например, "service.log").</param>
        public FileLogger(string directory, string fileName)
        {
            _filePath = Path.Combine(
                directory ?? throw new ArgumentNullException(nameof(directory)),
                fileName ?? throw new ArgumentNullException(nameof(fileName)));
        }

        /// <summary>
        /// Записывает строку в лог-файл с временной меткой.
        /// <paramref name="prefix"/> — опциональный тег (например, "[BOT]").
        /// Потокобезопасно: два одновременных вызова не испортят файл.
        /// </summary>
        public void Write(string message, string? prefix = null)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDirectoryExists();

                    TryRotateIfNeeded();

                    var p = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + " ";
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {p}{message}{Environment.NewLine}";
                    File.AppendAllText(_filePath, line);
                }
            }
            catch
            {
                // логирование не должно ронять приложение
            }
        }

        private void EnsureDirectoryExists()
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private void TryRotateIfNeeded()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var fileInfo = new FileInfo(_filePath);
                    if (fileInfo.Length > MaxSizeBytes)
                    {
                        var backupLog = _filePath + ".1";
                        if (File.Exists(backupLog))
                        {
                            File.Delete(backupLog);
                        }
                        File.Move(_filePath, backupLog);
                    }
                }
            }
            catch
            {
                // если ротация не удалась, продолжаем писать в текущий файл
            }
        }
    }
}
