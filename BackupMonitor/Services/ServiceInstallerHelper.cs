using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace BackupMonitor.Services
{
    [SupportedOSPlatform("windows")]
    public static class ServiceInstallerHelper
    {
        private static readonly string _serviceName = "BackupMonitorService";
        private static readonly string _defaultServiceInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), _serviceName);
        private static readonly string _defaultServiceConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), _serviceName);

        internal static string? ServiceInstallDirOverride { get; set; }
        internal static string? ServiceConfigDirOverride { get; set; }
        internal static string? SolutionDirectoryOverride { get; set; }
        internal static Func<bool>? IsRunningAsAdministratorOverride { get; set; }

        private static string ServiceInstallDir => ServiceInstallDirOverride ?? _defaultServiceInstallDir;
        private static string ServiceConfigDir => ServiceConfigDirOverride ?? _defaultServiceConfigDir;

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                var overrideCheck = IsRunningAsAdministratorOverride;
                if (overrideCheck != null)
                {
                    return overrideCheck();
                }

                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string? GetSolutionDirectory()
        {
            try
            {
                if (!string.IsNullOrEmpty(SolutionDirectoryOverride))
                {
                    return SolutionDirectoryOverride;
                }

                var currentDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                var directory = new DirectoryInfo(currentDir);
                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "BackupMonitor.sln")) ||
                        Directory.Exists(Path.Combine(directory.FullName, "BackupMonitorService")))
                    {
                        return directory.FullName;
                    }
                    directory = directory.Parent;
                }
                return null;
            }
            catch { return null; }
        }

        public static bool IsServiceExecutablePresent()
        {
            try
            {
                var exePath = Path.Combine(ServiceInstallDir, $"{_serviceName}.exe");
                return File.Exists(exePath);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsServicesConfigPresent()
        {
            try
            {
                var servicesPath = Path.Combine(ServiceConfigDir, "services.json");
                return File.Exists(servicesPath);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> BuildServiceProjectAsync(IProgress<string>? progress = null)
        {
            try
            {
                var solutionDir = GetSolutionDirectory();
                if (string.IsNullOrEmpty(solutionDir))
                {
                    progress?.Report("Не удалось найти директорию решения");
                    return false;
                }

                var serviceProjectPath = Path.Combine(solutionDir, "BackupMonitorService", "BackupMonitorService.csproj");
                if (!File.Exists(serviceProjectPath))
                {
                    progress?.Report($"Проект службы не найден: {serviceProjectPath}");
                    return false;
                }

                progress?.Report("Сборка проекта службы...");
                var processInfo = new ProcessStartInfo
                {
                    FileName = "dotnet.exe",
                    Arguments = $"build \"{serviceProjectPath}\" -c Release --no-incremental",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = solutionDir
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    progress?.Report("Не удалось запустить процесс сборки");
                    return false;
                }

                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    progress?.Report("Проект службы успешно собран");
                    return true;
                }
                else
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    progress?.Report($"Ошибка сборки: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Ошибка при сборке проекта: {ex.Message}");
                return false;
            }
        }

        public static async Task<string?> EnsureServiceExeExistsAsync(IProgress<string>? progress = null)
        {
            try
            {
                var solutionDir = GetSolutionDirectory();
                if (string.IsNullOrEmpty(solutionDir))
                {
                    progress?.Report("Не удалось найти директорию решения");
                    return null;
                }

                var possiblePath = Path.Combine(solutionDir, "BackupMonitorService", "bin", "Release", "net8.0-windows", $"{_serviceName}.exe");
                if (File.Exists(possiblePath))
                {
                    progress?.Report($"Найден файл службы: {possiblePath}");
                    return possiblePath;
                }

                progress?.Report("Файл службы не найден, начинаю сборку...");
                if (await BuildServiceProjectAsync(progress))
                {
                    if (File.Exists(possiblePath))
                    {
                        progress?.Report($"Файл службы собран: {possiblePath}");
                        return possiblePath;
                    }
                }

                progress?.Report("Не удалось найти или собрать файл службы");
                return null;
            }
            catch (Exception ex)
            {
                progress?.Report($"Ошибка при поиске файла службы: {ex.Message}");
                return null;
            }
        }

        internal static string? CopyServiceFilesToInstallDir(string serviceExePath, IProgress<string>? progress = null)
        {
            try
            {
                var sourceDirectory = Path.GetDirectoryName(serviceExePath);
                if (string.IsNullOrEmpty(sourceDirectory) || !Directory.Exists(sourceDirectory))
                {
                    progress?.Report($"ОШИБКА: Исходная директория службы не найдена: {sourceDirectory}");
                    return null;
                }

                progress?.Report($"Создание директории установки: {ServiceInstallDir}");
                Directory.CreateDirectory(ServiceInstallDir);

                progress?.Report("Копирование файлов службы...");
                var copiedCount = 0;
                var filesToCopy = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);

                foreach (var sourceFile in filesToCopy)
                {
                    try
                    {
                        var relativePath = sourceFile.Substring(sourceDirectory.Length + 1);
                        var targetFile = Path.Combine(ServiceInstallDir, relativePath);
                        
                        var targetDir = Path.GetDirectoryName(targetFile);
                        if(targetDir != null && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        
                        File.Copy(sourceFile, targetFile, true);
                        copiedCount++;
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"Не удалось скопировать файл {Path.GetFileName(sourceFile)}: {ex.Message}");
                    }
                }

                progress?.Report($"Скопировано {copiedCount} файлов в {ServiceInstallDir}");
                var targetExePath = Path.Combine(ServiceInstallDir, $"{_serviceName}.exe");
                return File.Exists(targetExePath) ? targetExePath : null;
            }
            catch (Exception ex)
            {
                progress?.Report($"Критическая ошибка при копировании файлов службы: {ex.Message}");
                return null;
            }
        }

        internal static bool CopyConfigFilesToConfigDir(string guiConfigDir, IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report($"Создание директории конфигурации: {ServiceConfigDir}");
                Directory.CreateDirectory(ServiceConfigDir);

                var configFiles = new[] { "services.json", "appconfig.json" };
                var allCopied = true;

                foreach (var configFile in configFiles)
                {
                    var sourcePath = Path.Combine(guiConfigDir, configFile);
                    var targetPath = Path.Combine(ServiceConfigDir, configFile);

                    if (File.Exists(sourcePath))
                    {
                        try
                        {
                            File.Copy(sourcePath, targetPath, true);
                            progress?.Report($"Скопирован файл конфигурации: {configFile} в {ServiceConfigDir}");
                        }
                        catch (Exception ex)
                        {
                            progress?.Report($"Не удалось скопировать {configFile}: {ex.Message}");
                            allCopied = false;
                        }
                    }
                    else
                    {
                        progress?.Report($"Файл конфигурации не найден в источнике: {sourcePath}");
                    }
                }
                return allCopied;
            }
            catch (Exception ex)
            {
                progress?.Report($"Ошибка при копировании конфигурационных файлов: {ex.Message}");
                return false;
            }
        }

        public static async Task<InstallResult> InstallServiceOneClickAsync(string configDirectory, WindowsServiceManager serviceManager, IProgress<string>? progress = null)
        {
            try
            {
                if (!IsRunningAsAdministrator())
                {
                    return new InstallResult { Success = false, ErrorMessage = "Для установки службы требуются права администратора." };
                }

                if (serviceManager.IsServiceInstalled())
                {
                    progress?.Report("Служба уже установлена. Выполняется переустановка...");
                    if (serviceManager.GetServiceStatus() == System.ServiceProcess.ServiceControllerStatus.Running)
                    {
                        serviceManager.StopService();
                        await Task.Delay(2000);
                    }
                    serviceManager.UninstallService();
                    await Task.Delay(1000);
                }

                progress?.Report("Поиск файла службы...");
                var serviceExePath = await EnsureServiceExeExistsAsync(progress);
                if (string.IsNullOrEmpty(serviceExePath))
                {
                    return new InstallResult { Success = false, ErrorMessage = "Не удалось найти или собрать BackupMonitorService.exe." };
                }

                progress?.Report("Подготовка файлов службы...");
                var installedExePath = CopyServiceFilesToInstallDir(serviceExePath, progress);
                if (string.IsNullOrEmpty(installedExePath))
                {
                    return new InstallResult { Success = false, ErrorMessage = $"Не удалось скопировать файлы службы в {ServiceInstallDir}." };
                }

                progress?.Report("Копирование конфигурационных файлов...");
                CopyConfigFilesToConfigDir(configDirectory, progress);
                
                var servicesJsonPath = Path.Combine(ServiceConfigDir, "services.json");
                if (!File.Exists(servicesJsonPath))
                {
                    progress?.Report("ВНИМАНИЕ: services.json не найден. Служба может не запуститься.");
                }

                progress?.Report("Установка службы Windows...");
                if (serviceManager.InstallService(installedExePath))
                {
                    progress?.Report("Служба установлена в системе.");
                    await Task.Delay(1000);
                    
                    progress?.Report("Запуск службы...");
                    if (serviceManager.StartService())
                    {
                        return new InstallResult { Success = true, Message = "Служба успешно установлена и запущена!" };
                    }
                    else
                    {
                        return new InstallResult { Success = true, Message = "Служба установлена, но не запустилась. Проверьте журнал событий Windows." };
                    }
                }
                else
                {
                    return new InstallResult { Success = false, ErrorMessage = "Не удалось установить службу. Проверьте, что запуск от имени администратора." };
                }
            }
            catch (Exception ex)
            {
                return new InstallResult { Success = false, ErrorMessage = $"Критическая ошибка при установке службы: {ex.Message}" };
            }
        }
    }

    public class InstallResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
