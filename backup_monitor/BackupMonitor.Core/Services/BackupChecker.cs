using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        public CheckResult CheckBackupForDate(Service service, DateTime targetDate)
        {
            var result = new CheckResult();

            try
            {
                if (!Directory.Exists(service.Path))
                {
                    result.ErrorMessage = $"Папка не найдена: {service.Path}";
                    return result;
                }

                var files = Directory.GetFiles(service.Path, "*", SearchOption.TopDirectoryOnly);
                
                if (files.Length == 0)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "В папке нет файлов";
                    return result;
                }
                
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    
                    // Проверяем наличие ключевых слов
                    if (!service.Keywords.Any(keyword => fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // Пытаемся извлечь дату из имени файла
                    var extractedDate = ExtractDateFromFileName(fileName, service.DatePatterns);
                    
                    // Проверяем, что дата извлечена и соответствует целевой дате
                    if (extractedDate.HasValue)
                    {
                        // Сравниваем только даты (без времени)
                        if (extractedDate.Value.Date == targetDate.Date)
                        {
                            result.IsValid = true;
                            return result;
                        }
                    }
                }

                result.IsValid = false;
            }
            catch (UnauthorizedAccessException)
            {
                result.ErrorMessage = "Нет доступа к папке";
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

            // Проверяем доступ к папке один раз в начале
            if (!Directory.Exists(service.Path))
            {
                result.ErrorMessage = $"Папка не найдена: {service.Path}";
                return result;
            }

            try
            {
                // Получаем список файлов один раз для оптимизации
                var files = Directory.GetFiles(service.Path, "*", SearchOption.TopDirectoryOnly);
                
                if (files.Length == 0)
                {
                    // Если файлов нет, все даты периода отсутствуют
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

                // Проверяем каждую дату периода
                var currentDate = startDate.Date;
                while (currentDate <= endDate.Date)
                {
                    var dayResult = CheckBackupForDateOptimized(service, currentDate, files);
                    if (!dayResult.IsValid)
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

        // Оптимизированная версия проверки для периода (использует предзагруженный список файлов)
        private CheckResult CheckBackupForDateOptimized(Service service, DateTime targetDate, string[] files)
        {
            var result = new CheckResult();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                
                // Проверяем наличие ключевых слов
                if (!service.Keywords.Any(keyword => fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Пытаемся извлечь дату из имени файла
                var extractedDate = ExtractDateFromFileName(fileName, service.DatePatterns);
                
                if (extractedDate.HasValue && extractedDate.Value.Date == targetDate.Date)
                {
                    result.IsValid = true;
                    return result;
                }
            }

            result.IsValid = false;
            return result;
        }

        private DateTime? ExtractDateFromFileName(string fileName, List<string> patterns)
        {
            var dateFromDayName = TryExtractDateWithDayOfWeek(fileName);
            if (dateFromDayName.HasValue)
            {
                return dateFromDayName;
            }

            // Сначала пробуем пользовательские паттерны
            foreach (var pattern in patterns)
            {
                try
                {
                    var match = Regex.Match(fileName, pattern);
                    if (match.Success)
                    {
                        // Извлекаем найденную строку
                        string dateString;
                        
                        // Groups[0] всегда содержит всё совпадение
                        // Если есть группы захвата (Groups.Count > 1), используем их
                        if (match.Groups.Count > 1)
                        {
                            // Если групп больше 1 (т.е. есть группы захвата), объединяем их
                            // Например, для паттерна (dd)(MM)(yyyy) будет 4 группы: [0]=всё, [1]=dd, [2]=MM, [3]=yyyy
                            if (match.Groups.Count >= 4)
                            {
                                // Объединяем все группы захвата (пропускаем группу 0)
                                dateString = string.Join("", match.Groups.Cast<Group>().Skip(1).Select(g => g.Value));
                            }
                            else
                            {
                                // Берём первую группу захвата (Groups[1])
                                dateString = match.Groups[1].Value;
                            }
                        }
                        else
                        {
                            // Нет групп захвата, берём всё совпадение
                            dateString = match.Value;
                        }
                        
                        if (string.IsNullOrEmpty(dateString))
                            continue;
                        
                        // Пытаемся распарсить дату в разных форматах
                        var parsedDate = TryParseDate(dateString);
                        if (parsedDate.HasValue)
                            return parsedDate;
                    }
                }
                catch (Exception)
                {
                    // Продолжаем с следующим паттерном при ошибке
                    continue;
                }
            }

            // Если пользовательские паттерны не сработали, пробуем стандартные
            return TryExtractDateWithStandardPatterns(fileName);
        }

        private DateTime? TryParseDate(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
                return null;

            // Формат yyyy_MM_dd (например: 2026_01_06 или 2026_01_01)
            if (DateTime.TryParseExact(dateString, "yyyy_MM_dd", null, System.Globalization.DateTimeStyles.None, out var date1))
                return date1;
            
            // Формат yyyy-MM-dd (например: 2026-01-06)
            if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date2))
                return date2;
            
            // Для 8 цифр подряд пробуем оба формата
            if (dateString.Length == 8 && dateString.All(char.IsDigit))
            {
                // Сначала пробуем ddMMyyyy (например: 01012026, 06012026, 07012026)
                if (DateTime.TryParseExact(dateString, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out var date3))
                    return date3;
                
                // Потом пробуем yyyyMMdd (например: 20260106)
                if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date4))
                    return date4;

                // Потом пробуем MMddyyyy (например: 01152026)
                if (DateTime.TryParseExact(dateString, "MMddyyyy", null, System.Globalization.DateTimeStyles.None, out var date5c))
                    return date5c;
            }

            // Также пробуем стандартный парсинг (на случай других форматов)
            if (DateTime.TryParse(dateString, out var date5))
                return date5;

            return null;
        }

        private DateTime? TryExtractDateWithDayOfWeek(string fileName)
        {
            // Формат: DayOfWeek + MMddyyyy (например: ARVANDFIN_Thu01082026_23361410)
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
            // Стандартные паттерны для автоматического поиска дат
            // yyyy_MM_dd (например: 2026_01_01 в Arvand_backup_2026_01_01_200002_3765753)
            var match1 = Regex.Match(fileName, @"(\d{4}_\d{2}_\d{2})");
            if (match1.Success)
            {
                var date = TryParseDate(match1.Groups[1].Value);
                if (date.HasValue) return date;
            }

            // yyyy-MM-dd (например: 2026-01-06 в arvand-2026-01-06- FullBackup.bak)
            var match2 = Regex.Match(fileName, @"(\d{4}-\d{2}-\d{2})");
            if (match2.Success)
            {
                var date = TryParseDate(match2.Groups[1].Value);
                if (date.HasValue) return date;
            }

            var dateFromDayName = TryExtractDateWithDayOfWeek(fileName);
            if (dateFromDayName.HasValue) return dateFromDayName;

            // ddMMyyyy - ищем 8 цифр подряд, где первые 2 цифры могут быть днём (01-31)
            // Например: 01012026, 06012026, 07012026 в JASDB_01012026, OnlineBank_06012026
            // Паттерн ищет: день(01-31) + месяц(01-12) + год(2000-2099)
            var match3 = Regex.Match(fileName, @"(0[1-9]|[12][0-9]|3[01])(0[1-9]|1[0-2])(20\d{2})");
            if (match3.Success && match3.Groups.Count >= 4)
            {
                // Объединяем группы: день + месяц + год = ddMMyyyy
                var dateString = match3.Groups[1].Value + match3.Groups[2].Value + match3.Groups[3].Value;
                var date = TryParseDate(dateString);
                if (date.HasValue) return date;
            }
            
            // Альтернативный паттерн для ddMMyyyy - более гибкий (год может быть любым 4-значным)
            var match3b = Regex.Match(fileName, @"(0[1-9]|[12][0-9]|3[01])(0[1-9]|1[0-2])(\d{4})");
            if (match3b.Success && match3b.Groups.Count >= 4)
            {
                var dateString = match3b.Groups[1].Value + match3b.Groups[2].Value + match3b.Groups[3].Value;
                var date = TryParseDate(dateString);
                if (date.HasValue) return date;
            }

            // yyyyMMdd - ищем 8 цифр, где первые 4 - год (например: 20260106)
            var match4 = Regex.Match(fileName, @"(20\d{2})(0[1-9]|1[0-2])(0[1-9]|[12][0-9]|3[01])");
            if (match4.Success && match4.Groups.Count >= 4)
            {
                // Объединяем группы: год + месяц + день = yyyyMMdd
                var dateString = match4.Groups[1].Value + match4.Groups[2].Value + match4.Groups[3].Value;
                var date = TryParseDate(dateString);
                if (date.HasValue) return date;
            }

            // Последняя попытка: ищем любые 8 цифр подряд и пробуем оба формата
            // Это должно найти "01012026" в "JASDB_01012026"
            var match5 = Regex.Match(fileName, @"(\d{8})");
            if (match5.Success)
            {
                var dateString = match5.Groups[1].Value;
                // Для 8 цифр пробуем сначала ddMMyyyy (более распространённый формат)
                if (dateString.Length == 8 && dateString.All(char.IsDigit))
                {
                    // Пробуем ddMMyyyy первым (например: 01012026 = 01.01.2026)
                    if (DateTime.TryParseExact(dateString, "ddMMyyyy", null, System.Globalization.DateTimeStyles.None, out var date5a))
                        return date5a;
                    
                    // Потом пробуем yyyyMMdd (например: 20260106 = 2026.01.06)
                    if (DateTime.TryParseExact(dateString, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date5b))
                        return date5b;

                    // Потом пробуем MMddyyyy (например: 01152026 = 15.01.2026)
                    if (DateTime.TryParseExact(dateString, "MMddyyyy", null, System.Globalization.DateTimeStyles.None, out var date5d))
                        return date5d;
                }
            }

            return null;
        }
    }
}
