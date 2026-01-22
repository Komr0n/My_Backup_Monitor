using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BackupMonitor.Core.Models;

namespace BackupMonitor.Core.Services
{
    public class BackupChecker
    {
        public class CheckResult
        {
            public bool IsValid { get; set; }
            public List<DateTime> MissingDates { get; set; } = new List<DateTime>();
            public string ErrorMessage { get; set; } = string.Empty;
        }

        public Task<ServiceCheckResult> CheckServiceAsync(Service service)
        {
            return CheckServiceAsync(service, DateTime.Today);
        }

        public Task<ServiceCheckResult> CheckServiceAsync(Service service, DateTime baseDate)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            if (service.Type == ServiceType.Group)
            {
                return CheckGroupAsync(service, baseDate);
            }

            var expectedDate = CalculateExpectedDate(service, baseDate);
            return Task.Run(() => CheckServiceForExpectedDate(service, expectedDate));
        }

        public CheckResult CheckBackupForDate(Service service, DateTime targetDate)
        {
            var result = new CheckResult();

            try
            {
                if (service.Type == ServiceType.Group)
                {
                    var groupResult = CheckGroupAsync(service, targetDate).GetAwaiter().GetResult();
                    result.IsValid = groupResult.Status == ServiceCheckStatus.OK;
                    result.ErrorMessage = groupResult.Status == ServiceCheckStatus.OK ? string.Empty : groupResult.Message;
                    return result;
                }

                var checkResult = CheckServiceForExpectedDate(service, targetDate.Date);
                result.IsValid = checkResult.Status == ServiceCheckStatus.OK;
                result.ErrorMessage = checkResult.Status == ServiceCheckStatus.OK ? string.Empty : checkResult.Message;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Ошибка: {ex.Message}";
            }

            return result;
        }

        public CheckResult CheckBackupForPeriod(Service service, DateTime startDate, DateTime endDate)
        {
            var result = new CheckResult();
            var missingDates = new List<DateTime>();

            if (service.Type == ServiceType.Group)
            {
                result.ErrorMessage = "Групповая проверка периода не поддерживается";
                return result;
            }

            if (!Directory.Exists(service.Path))
            {
                result.ErrorMessage = $"Папка не найдена: {service.Path}";
                return result;
            }

            try
            {
                var files = GetCandidateFiles(service);

                if (files.Count == 0)
                {
                    var date = startDate.Date;
                    while (date <= endDate.Date)
                    {
                        missingDates.Add(date);
                        date = date.AddDays(1);
                    }

                    result.IsValid = false;
                    result.MissingDates = missingDates;
                    result.ErrorMessage = "В папке нет файлов";
                    return result;
                }

                var minRequired = NormalizeMinRequired(service.MinFilesPerDay);
                var countsByDate = new Dictionary<DateTime, int>();
                var anyExtracted = false;

                foreach (var file in files)
                {
                    var date = TryGetFileDate(service, file);
                    if (!date.HasValue)
                    {
                        continue;
                    }

                    anyExtracted = true;
                    var day = date.Value.Date;
                    if (day < startDate.Date || day > endDate.Date)
                    {
                        continue;
                    }

                    countsByDate[day] = countsByDate.TryGetValue(day, out var count) ? count + 1 : 1;
                }

                if (!anyExtracted && service.CheckMode == ServiceCheckMode.NameDate)
                {
                    result.ErrorMessage = "Не удалось извлечь дату из имени файла";
                }

                var currentDate = startDate.Date;
                while (currentDate <= endDate.Date)
                {
                    var count = countsByDate.TryGetValue(currentDate, out var found) ? found : 0;
                    if (count < minRequired)
                    {
                        missingDates.Add(currentDate);
                    }

                    currentDate = currentDate.AddDays(1);
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.ErrorMessage = "Нет доступа к папке";
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Ошибка: {ex.Message}";
                return result;
            }

            result.IsValid = missingDates.Count == 0;
            result.MissingDates = missingDates;

            return result;
        }

        private static DateTime CalculateExpectedDate(Service service, DateTime baseDate)
        {
            return baseDate.Date.AddDays(-service.ExpectedDayOffset);
        }

        private async Task<ServiceCheckResult> CheckGroupAsync(Service service, DateTime baseDate)
        {
            var expectedDate = CalculateExpectedDate(service, baseDate);
            var result = new ServiceCheckResult
            {
                ServiceName = service.Name,
                ExpectedDate = expectedDate,
                MinRequiredCount = 0
            };

            var children = ResolveChildren(service);
            if (children.Count == 0)
            {
                result.Status = ServiceCheckStatus.FAIL;
                result.Message = "Группа не содержит дочерних сервисов";
                return result;
            }

            var childTasks = children.Select(child => CheckServiceAsync(child, baseDate)).ToArray();
            var childResults = await Task.WhenAll(childTasks);
            result.Children.AddRange(childResults);

            var requiredCount = 0;
            var requiredOk = 0;
            var requiredFail = 0;
            var optionalFail = 0;
            var hasError = false;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var childResult = childResults[i];

                if (childResult.Status == ServiceCheckStatus.ERROR)
                {
                    hasError = true;
                }

                if (child.Required)
                {
                    requiredCount++;
                    if (childResult.Status == ServiceCheckStatus.OK)
                    {
                        requiredOk++;
                    }
                    else if (childResult.Status == ServiceCheckStatus.FAIL)
                    {
                        requiredFail++;
                    }
                }
                else
                {
                    if (childResult.Status == ServiceCheckStatus.FAIL)
                    {
                        optionalFail++;
                    }
                }
            }

            if (hasError)
            {
                result.Status = ServiceCheckStatus.ERROR;
            }
            else if (requiredFail > 0)
            {
                result.Status = ServiceCheckStatus.FAIL;
            }
            else if (optionalFail > 0)
            {
                result.Status = ServiceCheckStatus.WARNING;
            }
            else
            {
                result.Status = ServiceCheckStatus.OK;
            }

            result.Message = $"Обязательные: {requiredOk}/{requiredCount} OK; необязательные FAIL: {optionalFail}";
            result.Details.AddRange(BuildGroupDetails(children, childResults));
            result.LastObservedBackupDate = childResults
                .Where(r => r.LastObservedBackupDate.HasValue)
                .Select(r => r.LastObservedBackupDate!.Value)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            return result;
        }

        private List<Service> ResolveChildren(Service service)
        {
            if (service.Children != null && service.Children.Count > 0)
            {
                return service.Children;
            }

            if (service.ChildFolders == null || service.ChildFolders.Count == 0)
            {
                return new List<Service>();
            }

            var children = new List<Service>();
            foreach (var folder in service.ChildFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                var childName = folder.Trim();
                var childKeywords = (service.Keywords != null && service.Keywords.Count > 0)
                    ? new List<string>(service.Keywords)
                    : (service.UseChildFolderAsKeyword ? new List<string> { childName } : new List<string>());

                var child = new Service
                {
                    Name = childName,
                    Path = Path.Combine(service.Path, childName),
                    Keywords = childKeywords,
                    DatePatterns = new List<string>(service.DatePatterns ?? new List<string>()),
                    ExpectedDayOffset = service.ExpectedDayOffset,
                    CheckMode = service.CheckMode,
                    FileTimeSource = service.FileTimeSource,
                    MinFilesPerDay = service.MinFilesPerDay,
                    FileMask = service.FileMask,
                    Type = ServiceType.Single,
                    Required = true
                };

                children.Add(child);
            }

            return children;
        }

        private static IEnumerable<string> BuildGroupDetails(IReadOnlyList<Service> children, IReadOnlyList<ServiceCheckResult> results)
        {
            var details = new List<string>();
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var result = results[i];
                if (result.Status == ServiceCheckStatus.OK)
                {
                    continue;
                }

                var message = string.IsNullOrWhiteSpace(result.Message) ? result.Status.ToString() : result.Message;
                details.Add($"{child.Name}: {result.Status} ({message})");
            }

            return details;
        }

        private ServiceCheckResult CheckServiceForExpectedDate(Service service, DateTime expectedDate)
        {
            var result = new ServiceCheckResult
            {
                ServiceName = service.Name,
                ExpectedDate = expectedDate,
                MinRequiredCount = NormalizeMinRequired(service.MinFilesPerDay)
            };

            try
            {
                if (!Directory.Exists(service.Path))
                {
                    result.Status = ServiceCheckStatus.ERROR;
                    result.Message = $"Папка не найдена: {service.Path}";
                    return result;
                }

                var files = GetCandidateFiles(service);

                if (files.Count == 0)
                {
                    result.Status = ServiceCheckStatus.FAIL;
                    result.Message = service.Keywords != null && service.Keywords.Count > 0
                        ? "Нет файлов по ключевым словам"
                        : "В папке нет файлов";
                    return result;
                }

                var foundCount = 0;
                var extractedCount = 0;
                DateTime? lastObserved = null;

                foreach (var file in files)
                {
                    var date = TryGetFileDate(service, file);
                    if (!date.HasValue)
                    {
                        continue;
                    }

                    extractedCount++;
                    var day = date.Value.Date;
                    if (!lastObserved.HasValue || day > lastObserved.Value)
                    {
                        lastObserved = day;
                    }

                    if (day == expectedDate.Date)
                    {
                        foundCount++;
                    }
                }

                result.FoundCount = foundCount;
                result.LastObservedBackupDate = lastObserved;

                if (service.CheckMode == ServiceCheckMode.NameDate && extractedCount == 0)
                {
                    result.Status = ServiceCheckStatus.FAIL;
                    result.Message = "Не удалось извлечь дату из имени файла";
                    result.Details.Add("Проверьте регулярные выражения и формат имени файла");
                    return result;
                }

                if (foundCount >= result.MinRequiredCount)
                {
                    result.Status = ServiceCheckStatus.OK;
                    result.Message = $"Найдено файлов за {expectedDate:yyyy-MM-dd}: {foundCount}";
                }
                else
                {
                    result.Status = ServiceCheckStatus.FAIL;
                    result.Message = $"Нет файлов за {expectedDate:yyyy-MM-dd}";
                    result.Details.Add($"Найдено: {foundCount} из {result.MinRequiredCount}");
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Status = ServiceCheckStatus.ERROR;
                result.Message = "Нет доступа к папке";
            }
            catch (Exception ex)
            {
                result.Status = ServiceCheckStatus.ERROR;
                result.Message = $"Ошибка: {ex.Message}";
            }

            return result;
        }

        private List<string> GetCandidateFiles(Service service)
        {
            var mask = string.IsNullOrWhiteSpace(service.FileMask) ? "*" : service.FileMask.Trim();
            var files = Directory.GetFiles(service.Path, mask, SearchOption.TopDirectoryOnly);

            if (service.Keywords == null || service.Keywords.Count == 0)
            {
                return files.ToList();
            }

            return files
                .Where(file =>
                {
                    var fileName = Path.GetFileName(file);
                    return service.Keywords.Any(keyword =>
                        fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
        }

        private DateTime? TryGetFileDate(Service service, string filePath)
        {
            try
            {
                if (service.CheckMode == ServiceCheckMode.FileTime)
                {
                    var fileTime = service.FileTimeSource == FileTimeSource.CreationTime
                        ? File.GetCreationTime(filePath)
                        : File.GetLastWriteTime(filePath);
                    return fileTime.Date;
                }

                var fileName = Path.GetFileName(filePath);
                return ExtractDateFromFileName(fileName, service.DatePatterns ?? new List<string>());
            }
            catch
            {
                return null;
            }
        }

        private static int NormalizeMinRequired(int minRequired)
        {
            return minRequired <= 0 ? 1 : minRequired;
        }

        private DateTime? ExtractDateFromFileName(string fileName, List<string> patterns)
        {
            var dateFromDayName = TryExtractDateWithDayOfWeek(fileName);
            if (dateFromDayName.HasValue)
            {
                return dateFromDayName;
            }

            foreach (var pattern in patterns)
            {
                try
                {
                    var match = Regex.Match(fileName, pattern);
                    if (match.Success)
                    {
                        string dateString;

                        if (match.Groups.Count > 1)
                        {
                            if (match.Groups.Count >= 4)
                            {
                                dateString = string.Join("", match.Groups.Cast<Group>().Skip(1).Select(g => g.Value));
                            }
                            else
                            {
                                dateString = match.Groups[1].Value;
                            }
                        }
                        else
                        {
                            dateString = match.Value;
                        }

                        if (string.IsNullOrEmpty(dateString))
                            continue;

                        var parsedDate = TryParseDate(dateString);
                        if (parsedDate.HasValue)
                            return parsedDate;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return TryExtractDateWithStandardPatterns(fileName);
        }

        private DateTime? TryParseDate(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
                return null;

            if (DateTime.TryParseExact(dateString, "yyyy_MM_dd", null, System.Globalization.DateTimeStyles.None, out var date1))
                return date1;

            if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date2))
                return date2;

            if (dateString.Length == 8 && dateString.All(char.IsDigit))
            {
                if (DateTime.TryParseExact(dateString, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out var date3))
                    return date3;

                if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date4))
                    return date4;

                if (DateTime.TryParseExact(dateString, "MMddyyyy", null, System.Globalization.DateTimeStyles.None, out var date5c))
                    return date5c;
            }

            if (DateTime.TryParse(dateString, out var date5))
                return date5;

            return null;
        }

        private DateTime? TryExtractDateWithDayOfWeek(string fileName)
        {
            var match = Regex.Match(fileName, @"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)(\d{8})", RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups.Count < 2)
                return null;

            var digits = match.Groups[1].Value;
            if (digits.Length != 8 || !digits.All(char.IsDigit))
                return null;

            var dayToken = match.Value.Substring(0, 3).ToLowerInvariant();
            DayOfWeek? expectedDay = dayToken switch
            {
                "mon" => DayOfWeek.Monday,
                "tue" => DayOfWeek.Tuesday,
                "wed" => DayOfWeek.Wednesday,
                "thu" => DayOfWeek.Thursday,
                "fri" => DayOfWeek.Friday,
                "sat" => DayOfWeek.Saturday,
                "sun" => DayOfWeek.Sunday,
                _ => null
            };

            if (!expectedDay.HasValue)
                return null;

            if (DateTime.TryParseExact(digits, "MMddyyyy", null, System.Globalization.DateTimeStyles.None, out var mmdd)
                && mmdd.DayOfWeek == expectedDay.Value)
            {
                return mmdd;
            }

            if (DateTime.TryParseExact(digits, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out var ddmm)
                && ddmm.DayOfWeek == expectedDay.Value)
            {
                return ddmm;
            }

            return null;
        }

        private DateTime? TryExtractDateWithStandardPatterns(string fileName)
        {
            var match1 = Regex.Match(fileName, @"(\d{4}_\d{2}_\d{2})");
            if (match1.Success)
            {
                var date = TryParseDate(match1.Groups[1].Value);
                if (date.HasValue) return date;
            }

            var match2 = Regex.Match(fileName, @"(\d{4}-\d{2}-\d{2})");
            if (match2.Success)
            {
                var date = TryParseDate(match2.Groups[1].Value);
                if (date.HasValue) return date;
            }

            var dateFromDayName = TryExtractDateWithDayOfWeek(fileName);
            if (dateFromDayName.HasValue) return dateFromDayName;

            var match3 = Regex.Match(fileName, @"(0[1-9]|[12][0-9]|3[01])(0[1-9]|1[0-2])(20\d{2})");
            if (match3.Success && match3.Groups.Count >= 4)
            {
                var dateString = match3.Groups[1].Value + match3.Groups[2].Value + match3.Groups[3].Value;
                var date = TryParseDate(dateString);
                if (date.HasValue) return date;
            }

            var match3b = Regex.Match(fileName, @"(0[1-9]|[12][0-9]|3[01])(0[1-9]|1[0-2])(\d{4})");
            if (match3b.Success && match3b.Groups.Count >= 4)
            {
                var dateString = match3b.Groups[1].Value + match3b.Groups[2].Value + match3b.Groups[3].Value;
                var date = TryParseDate(dateString);
                if (date.HasValue) return date;
            }

            var match4 = Regex.Match(fileName, @"(20\d{2})(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])");
            if (match4.Success && match4.Groups.Count >= 4)
            {
                var dateString = match4.Groups[1].Value + match4.Groups[2].Value + match4.Groups[3].Value;
                var date = TryParseDate(dateString);
                if (date.HasValue) return date;
            }

            var match5 = Regex.Match(fileName, @"(\d{8})");
            if (match5.Success)
            {
                var dateString = match5.Groups[1].Value;
                if (dateString.Length == 8 && dateString.All(char.IsDigit))
                {
                    if (DateTime.TryParseExact(dateString, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out var date5a))
                        return date5a;

                    if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date5b))
                        return date5b;

                    if (DateTime.TryParseExact(dateString, "MMddyyyy", null, System.Globalization.DateTimeStyles.None, out var date5d))
                        return date5d;
                }
            }

            return null;
        }
    }
}
