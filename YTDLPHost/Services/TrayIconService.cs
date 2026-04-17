using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using Application = System.Windows.Application;

namespace YTDLPHost.Services
{
    public class TrayIconService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private bool _disposed;

        public event EventHandler? ShowWindowRequested;
        public event EventHandler? ExitRequested;

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

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var showItem = new System.Windows.Forms.ToolStripMenuItem("Show Window");
            showItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
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

        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }

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