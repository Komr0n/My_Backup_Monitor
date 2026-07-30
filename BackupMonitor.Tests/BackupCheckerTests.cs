using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BackupMonitor.Core.Models;
using BackupMonitor.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupMonitor.Tests
{
    [TestClass]
    public class BackupCheckerTests
    {
        [TestMethod]
        public async Task NameDate_CheckCounts_MinFiles()
        {
            using var temp = new TempDirectory();
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "db_backup_2026_01_21_1.bak"), "x");
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "db_backup_2026_01_21_2.bak"), "x");
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "db_backup_2026_01_20.bak"), "x");

            var service = new Service
            {
                Name = "NameDate",
                Path = temp.DirectoryPath,
                CheckMode = ServiceCheckMode.NameDate,
                DatePatterns = new List<string> { @"(\d{4}_\d{2}_\d{2})" },
                MinFilesPerDay = 2
            };

            var checker = new BackupChecker();
            var baseDate = new DateTime(2026, 1, 21);
            var result = await checker.CheckServiceAsync(service, baseDate);

            Assert.AreEqual(ServiceCheckStatus.OK, result.Status);
            Assert.AreEqual(2, result.FoundCount);
        }

        [TestMethod]
        public async Task FileTime_UsesLastWriteTime()
        {
            using var temp = new TempDirectory();
            var file = Path.Combine(temp.DirectoryPath, "backup_1.bak");
            File.WriteAllText(file, "x");
            File.SetLastWriteTime(file, new DateTime(2026, 1, 21, 10, 0, 0));

            var service = new Service
            {
                Name = "FileTime",
                Path = temp.DirectoryPath,
                CheckMode = ServiceCheckMode.FileTime,
                FileTimeSource = FileTimeSource.LastWriteTime,
                MinFilesPerDay = 1
            };

            var checker = new BackupChecker();
            var baseDate = new DateTime(2026, 1, 21);
            var result = await checker.CheckServiceAsync(service, baseDate);

            Assert.AreEqual(ServiceCheckStatus.OK, result.Status);
            Assert.AreEqual(1, result.FoundCount);
        }

        [TestMethod]
        public async Task ExpectedDayOffset_ShiftsExpectedDate()
        {
            using var temp = new TempDirectory();
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "backup_2026_01_21.bak"), "x");

            var service = new Service
            {
                Name = "Offset",
                Path = temp.DirectoryPath,
                CheckMode = ServiceCheckMode.NameDate,
                DatePatterns = new List<string> { @"(\d{4}_\d{2}_\d{2})" },
                ExpectedDayOffset = 1
            };

            var checker = new BackupChecker();
            var baseDate = new DateTime(2026, 1, 22);
            var result = await checker.CheckServiceAsync(service, baseDate);

            Assert.AreEqual(new DateTime(2026, 1, 21), result.ExpectedDate.Date);
            Assert.AreEqual(ServiceCheckStatus.OK, result.Status);
        }

        [TestMethod]
        public async Task NameDate_DotFormat_ddMMyy_Detected()
        {
            // Формат 30.07.26 — самый частый в РФ (день.месяц.2-значный год)
            using var temp = new TempDirectory();
            var targetDate = new DateTime(2026, 7, 30);
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "backup_30.07.26.bak"), "data123456");
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "backup_29.07.26.bak"), "data123456");

            var service = new Service
            {
                Name = "DotFormat",
                Path = temp.DirectoryPath,
                CheckMode = ServiceCheckMode.NameDate,
                DatePatterns = new List<string>(), // полагаемся на стандартные паттерны
                ExpectedDayOffset = 0,
                MinFilesPerDay = 1
            };

            var checker = new BackupChecker();
            var result = await checker.CheckServiceAsync(service, targetDate);

            Assert.AreEqual(ServiceCheckStatus.OK, result.Status,
                $"Ожидался OK для 30.07.26. Status={result.Status}, Message={result.Message}");
        }

        [TestMethod]
        public async Task NameDate_DotFormat_ddMMyyyy_Detected()
        {
            // Формат 30.07.2026 — день.месяц.4-значный год
            using var temp = new TempDirectory();
            var targetDate = new DateTime(2026, 7, 30);
            File.WriteAllText(Path.Combine(temp.DirectoryPath, "db_30.07.2026.sql"), "data123456");

            var service = new Service
            {
                Name = "DotFullFormat",
                Path = temp.DirectoryPath,
                CheckMode = ServiceCheckMode.NameDate,
                DatePatterns = new List<string>(),
                ExpectedDayOffset = 0,
                MinFilesPerDay = 1
            };

            var checker = new BackupChecker();
            var result = await checker.CheckServiceAsync(service, targetDate);

            Assert.AreEqual(ServiceCheckStatus.OK, result.Status,
                $"Ожидался OK для 30.07.2026. Status={result.Status}, Message={result.Message}");
        }

        [TestMethod]
        public async Task Group_RequiredFail_MakesGroupFail()
        {
            using var temp = new TempDirectory();
            var okDir = temp.CreateSubdirectory("ok");
            var failDir = temp.CreateSubdirectory("fail");

            var okFile = Path.Combine(okDir, "backup_ok.bak");
            File.WriteAllText(okFile, "x");
            File.SetLastWriteTime(okFile, new DateTime(2026, 1, 21, 1, 0, 0));

            var group = new Service
            {
                Name = "Group",
                Type = ServiceType.Group,
                Children = new List<Service>
                {
                    new Service
                    {
                        Name = "RequiredOk",
                        Path = okDir,
                        CheckMode = ServiceCheckMode.FileTime,
                        FileTimeSource = FileTimeSource.LastWriteTime,
                        Required = true
                    },
                    new Service
                    {
                        Name = "RequiredFail",
                        Path = failDir,
                        CheckMode = ServiceCheckMode.FileTime,
                        FileTimeSource = FileTimeSource.LastWriteTime,
                        Required = true
                    }
                }
            };

            var checker = new BackupChecker();
            var baseDate = new DateTime(2026, 1, 21);
            var result = await checker.CheckServiceAsync(group, baseDate);

            Assert.AreEqual(ServiceCheckStatus.FAIL, result.Status);
        }

        [TestMethod]
        public async Task Group_OptionalFail_MakesGroupWarning()
        {
            using var temp = new TempDirectory();
            var okDir = temp.CreateSubdirectory("ok");
            var optionalDir = temp.CreateSubdirectory("optional");

            var okFile = Path.Combine(okDir, "backup_ok.bak");
            File.WriteAllText(okFile, "x");
            File.SetLastWriteTime(okFile, new DateTime(2026, 1, 21, 1, 0, 0));

            var group = new Service
            {
                Name = "Group",
                Type = ServiceType.Group,
                Children = new List<Service>
                {
                    new Service
                    {
                        Name = "RequiredOk",
                        Path = okDir,
                        CheckMode = ServiceCheckMode.FileTime,
                        FileTimeSource = FileTimeSource.LastWriteTime,
                        Required = true
                    },
                    new Service
                    {
                        Name = "OptionalFail",
                        Path = optionalDir,
                        CheckMode = ServiceCheckMode.FileTime,
                        FileTimeSource = FileTimeSource.LastWriteTime,
                        Required = false
                    }
                }
            };

            var checker = new BackupChecker();
            var baseDate = new DateTime(2026, 1, 21);
            var result = await checker.CheckServiceAsync(group, baseDate);

            Assert.AreEqual(ServiceCheckStatus.WARNING, result.Status);
        }

        private sealed class TempDirectory : IDisposable
        {
            public TempDirectory()
            {
                DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryPath);
            }

            public string DirectoryPath { get; }

            public string CreateSubdirectory(string name)
            {
                var dir = Path.Combine(DirectoryPath, name);
                Directory.CreateDirectory(dir);
                return dir;
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(DirectoryPath))
                    {
                        Directory.Delete(DirectoryPath, true);
                    }
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
    }
}
