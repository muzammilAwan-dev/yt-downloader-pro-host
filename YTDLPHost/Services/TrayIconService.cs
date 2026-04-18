using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;

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

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadApplicationIcon() ?? SystemIcons.Application,
                Text = "YT Downloader Pro",
                Visible = true
            };

            // FIX: Restore window on Left-Click
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                }
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
        }

        private Icon? LoadApplicationIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("YTDLPHost.Assets.icon.ico");
                if (stream != null) return new Icon(stream);
            }
            catch { }
            return null;
        }

        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null) 
            {
                _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text;
            }
        }

        public void ShowDownloadCompleteNotification(string title, string fileName)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("Download Complete")
                    .AddText($"\"{fileName}\" finished.")
                    .AddAttributionText("YT Downloader Pro")
                    .Show();
            }
            catch
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.BalloonTipTitle = "Download Complete";
                    _notifyIcon.BalloonTipText = fileName;
                    _notifyIcon.ShowBalloonTip(3000);
                }
            }
        }

        public void SetVisible(bool visible)
        {
            if (_notifyIcon != null) _notifyIcon.Visible = visible;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { ToastNotificationManagerCompat.History.Clear(); } catch { }
            
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
    }
}
