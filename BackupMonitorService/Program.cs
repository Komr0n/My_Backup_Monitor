using System;
using System.IO;
using BackupMonitor.Core.Services;
using BackupMonitorService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Устанавливаем текущую директорию как директорию, где находится exe.
// Это критически важно для корректной работы службы Windows.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

const string serviceName = "BackupMonitorService";
// Конфигурация всегда хранится в ProgramData
var configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), serviceName);

try
{
    // Попытка зарегистрировать источник в Event Log
    if (!System.Diagnostics.EventLog.SourceExists(serviceName))
    {
        System.Diagnostics.EventLog.CreateEventSource(serviceName, "Application");
    }
}
catch (Exception ex)
{
    // Логируем ошибку, но не прерываем выполнение. 
    // Служба сможет работать, но логи будут писаться в общий источник "Application".
    Console.WriteLine($"Warning: Could not create EventLog source. {ex.Message}");
}

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = serviceName;
    })
    .ConfigureServices((hostContext, services) =>
    {
        // Регистрируем сервисы для Dependency Injection
        
        // ConfigurationManager как Singleton, чтобы все использовали один и тот же экземпляр
        services.AddSingleton(new BackupMonitor.Core.Services.ConfigurationManager(configDirectory));

        // BackupChecker и TelegramReportSender также как Singleton
        services.AddSingleton<BackupChecker>();
        services.AddSingleton<TelegramReportSender>();

        // Основной воркер службы
        services.AddHostedService<BackupMonitorWorker>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole(); // Для отладки при локальном запуске
        logging.AddEventLog(settings => // Для логирования при работе в качестве службы
        {
            settings.SourceName = serviceName;
            settings.LogName = "Application";
        });
        logging.SetMinimumLevel(LogLevel.Information); // Устанавливаем минимальный уровень логирования
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("{ServiceName} is starting.", serviceName);
logger.LogInformation("Service executable path: {path}", AppContext.BaseDirectory);
logger.LogInformation("Service configuration directory: {path}", configDirectory);

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "A critical error occurred while running the service.");
    // Пробрасываем исключение, чтобы Windows Service Manager знал об ошибке
    throw;
}
