using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace BackupMonitor.Services
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }

    /// <summary>
    /// Управляет иконкой в системном трее: контекстное меню, состояние службы
    /// (на основе heartbeat-файла), быстрый доступ к проверке и выходу.
    /// Использует WinForms NotifyIcon (UseWindowsForms уже включён — без новых NuGet).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class TrayIconManager : IDisposable
    {
        private const string ServiceName = "BackupMonitorService";

        private static readonly string ServiceConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ServiceName);

        private static readonly string HeartbeatPath = Path.Combine(ServiceConfigDir, ".heartbeat");

        private const int HeartbeatFreshSeconds = 120; // 2 минуты

        private readonly WinForms.NotifyIcon _notifyIcon;
        private readonly WinForms.Timer _heartbeatTimer;
        private readonly Action _showMainWindow;
        private readonly Action? _runCheck;

        private bool _disposed;

        public TrayIconManager(Action showMainWindow, Action? runCheck = null)
        {
            _showMainWindow = showMainWindow ?? throw new ArgumentNullException(nameof(showMainWindow));
            _runCheck = runCheck;

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = CreateStatusIcon(TrayStatus.Unknown),
                Visible = true,
                Text = "Backup Monitor"
            };

            BuildContextMenu();
            _notifyIcon.DoubleClick += (s, e) => _showMainWindow();

            // Обновляем состояние службы каждые 30 секунд
            _heartbeatTimer = new WinForms.Timer { Interval = 30000 };
            _heartbeatTimer.Tick += (s, e) => RefreshStatus();
            _heartbeatTimer.Start();

            // Первичное обновление
            RefreshStatus();
        }

        private void BuildContextMenu()
        {
            var menu = new WinForms.ContextMenuStrip();

            var showItem = new WinForms.ToolStripMenuItem("Показать окно");
            showItem.Click += (s, e) => _showMainWindow();
            menu.Items.Add(showItem);

            if (_runCheck != null)
            {
                var checkItem = new WinForms.ToolStripMenuItem("▶ Проверить сегодня");
                checkItem.Click += (s, e) => _runCheck();
                menu.Items.Add(checkItem);
            }

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var statusItem = new WinForms.ToolStripMenuItem("⟳ Обновить статус службы");
            statusItem.Click += (s, e) => RefreshStatus();
            menu.Items.Add(statusItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var exitItem = new WinForms.ToolStripMenuItem("Выход");
            exitItem.Click += (s, e) =>
            {
                _notifyIcon.Visible = false;
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            };
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        /// <summary>
        /// Читает heartbeat-файл и статус службы, обновляет иконку и tooltip.
        /// Старая иконка диспозится чтобы избежать утечки GDI-хэндлов.
        /// </summary>
        public void RefreshStatus()
        {
            var status = EvaluateServiceStatus();
            var newIcon = CreateStatusIcon(status);
            var oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = newIcon;
            oldIcon?.Dispose();
            _notifyIcon.Text = BuildTooltip(status);
        }

        private enum TrayStatus
        {
            Running,       // служба работает (heartbeat свежий)
            Stopped,       // установлена, но не запущена
            NotInstalled,  // не установлена
            Unknown        // не удалось определить
        }

        private static TrayStatus EvaluateServiceStatus()
        {
            try
            {
                // Сначала проверяем, установлена ли служба
                using var sc = new ServiceController(ServiceName);
                var scStatus = sc.Status;
                if (scStatus == ServiceControllerStatus.Running)
                {
                    // Дополнительная проверка по heartbeat — реально жива ли служба
                    var heartbeatAge = GetHeartbeatAge();
                    if (heartbeatAge.HasValue && heartbeatAge.Value.TotalSeconds <= HeartbeatFreshSeconds)
                        return TrayStatus.Running;
                    // Heartbeat старый/отсутствует, но служба "Running" — возможно, зависла
                    return TrayStatus.Stopped;
                }
                return TrayStatus.Stopped;
            }
            catch (InvalidOperationException)
            {
                // Служба не установлена
                return TrayStatus.NotInstalled;
            }
            catch
            {
                return TrayStatus.Unknown;
            }
        }

        private static TimeSpan? GetHeartbeatAge()
        {
            try
            {
                if (!File.Exists(HeartbeatPath)) return null;
                var text = File.ReadAllText(HeartbeatPath).Trim();
                if (DateTime.TryParse(text, out var ts))
                {
                    return DateTime.Now - ts;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static string BuildTooltip(TrayStatus status)
        {
            var prefix = "Backup Monitor";
            return status switch
            {
                TrayStatus.Running => $"{prefix} — Служба работает",
                TrayStatus.Stopped => $"{prefix} — Служба остановлена",
                TrayStatus.NotInstalled => $"{prefix} — Служба не установлена",
                _ => $"{prefix}"
            };
        }

        /// <summary>
        /// Создаёт простую цветную иконку состояния (круг) на лету — без ресурсов.
        /// Размер 32×32 — минимально видимый на мониторах с 125-200% DPI.
        /// GetHicon выделяет неуправляемый HICON; Icon.FromHandle НЕ забирает
        /// владение — нужно вызвать DestroyIcon во избежание утечки GDI.
        /// </summary>
        private static Icon CreateStatusIcon(TrayStatus status)
        {
            Color color = status switch
            {
                TrayStatus.Running => Color.FromArgb(46, 125, 50),      // зелёный
                TrayStatus.Stopped => Color.FromArgb(245, 124, 0),      // оранжевый
                TrayStatus.NotInstalled => Color.FromArgb(158, 158, 158), // серый
                _ => Color.FromArgb(158, 158, 158)
            };

            const int size = 32;
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, 4, 4, size - 8, size - 8);
                using var pen = new Pen(Color.White, 2);
                g.DrawEllipse(pen, 4, 4, size - 8, size - 8);
            }

            var handle = bmp.GetHicon();
            try
            {
                // ВАЖНО: Icon.FromHandle НЕ забирает владение HICON — это лишь обёртка.
                // Если сразу вызвать DestroyIcon (как было раньше), иконка-зомби
                // отображается в трее как пустое место (tooltip при этом работает).
                // Clone() создаёт независимую иконку, владеющую собственным дескриптором,
                // после чего оригинальный HICON можно безопасно уничтожить.
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }

        public void ShowBalloon(string title, string message, int timeoutMs = 3000)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(timeoutMs);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                // NotifyIcon.Dispose() НЕ диспозит назначенную Icon —
                // иначе текущая иконка утекает (один HICON за запуск).
                _notifyIcon.Icon?.Dispose();
                _notifyIcon.Dispose();
            }
        }
    }
}
