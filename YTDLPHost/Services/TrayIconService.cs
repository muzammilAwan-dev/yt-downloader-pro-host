using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using Application = System.Windows.Application;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Manages the system tray icon, context menu operations, and Windows toast notifications.
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private bool _disposed;

        public event EventHandler? ShowWindowRequested;
        public event EventHandler? ExitRequested;

        /// <summary>
        /// Initializes the tray icon, binds click events, and configures the context menu.
        /// </summary>
        public void Initialize()
        {
            if (_notifyIcon != null) return;

            var icon = LoadApplicationIcon();

            _notifyIcon = new NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Application,
                Text = "YT Downloader Pro",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("Show Window");
            showItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                }
            };

            _notifyIcon.DoubleClick += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

            try
            {
                ToastNotificationManagerCompat.OnActivated += toastArgs =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                    });
                };
            }
            catch { }
        }

        /// <summary>
        /// Attempts to load the application icon from embedded resources or the executable path.
        /// </summary>
        private static System.Drawing.Icon? LoadApplicationIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("YTDLPHost.Assets.icon.ico");
                if (stream != null)
                {
                    return new System.Drawing.Icon(stream);
                }
            }
            catch { }

            try
            {
                var exePath = Environment.ProcessPath;
                var exeDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var iconPath = Path.Combine(exeDir, "Assets", "icon.ico");
                    if (File.Exists(iconPath))
                    {
                        return new System.Drawing.Icon(iconPath);
                    }
                }
            }
            catch { }

            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    return System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Updates the tray icon hover tooltip text. Constrains to Windows length limits.
        /// </summary>
        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }

        /// <summary>
        /// Displays a standard Windows notification balloon or modern toast.
        /// </summary>
        public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (_notifyIcon == null) return;

            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .AddAttributionText("YT Downloader Pro")
                    .Show();
            }
            catch
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = message;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(3000);
            }
        }

        /// <summary>
        /// Triggers a completion notification using Windows Toasts with a balloon fallback.
        /// </summary>
        public void ShowDownloadCompleteNotification(string title, string fileName)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("Download Complete")
                    .AddText($"\"{fileName}\" finished downloading.")
                    .AddAttributionText("YT Downloader Pro")
                    .AddArgument("action", "show")
                    .Show();
            }
            catch
            {
                ShowBalloon("Download Complete", $"\"{fileName}\" finished downloading.", ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// Toggles the visibility of the tray icon.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_notifyIcon != null)
                _notifyIcon.Visible = visible;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                ToastNotificationManagerCompat.History.Clear();
            }
            catch { }

            _notifyIcon?.Dispose();
            _notifyIcon = null;
        }
    }
}
