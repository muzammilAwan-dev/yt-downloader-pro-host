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

        // Events to communicate back to the MainViewModel/App
        public event EventHandler? ShowWindowRequested;
        public event EventHandler? ExitRequested;

        public void Initialize()
        {
            if (_notifyIcon != null) return;

            // Initialize the NotifyIcon with the application's embedded icon
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadApplicationIcon() ?? SystemIcons.Application,
                Text = "YT Downloader Pro - Idle",
                Visible = true
            };

            [cite_start]// FIX: Restore the window immediately on a single Left-Click [cite: 1]
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                }
            };

            // Setup the Right-Click Context Menu
            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("Show Window");
            showItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

            var exitItem = new ToolStripMenuItem("Exit Application");
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        /// <summary>
        /// Loads the icon.ico file from the project's embedded resources.
        /// Ensure the icon's Build Action is set to "Embedded Resource".
        /// </summary>
        private Icon? LoadApplicationIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                // Assumes the icon is at Assets/icon.ico in your project structure
                using var stream = assembly.GetManifestResourceStream("YTDLPHost.Assets.icon.ico");
                if (stream != null) return new Icon(stream);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Updates the hover text seen when mousing over the tray icon.
        /// Windows limits this to 64 characters.
        /// </summary>
        public void UpdateTooltip(string text)
        {
            if (_notifyIcon != null)
            {
                // Safety truncation for Windows character limits
                _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text;
            }
        }

        /// <summary>
        /// Sends a Windows Toast notification when a download finishes.
        /// </summary>
        public void ShowDownloadCompleteNotification(string title, string fileName)
        {
            try
            {
                [cite_start]// Attempting modern Windows 10/11 Toast [cite: 2]
                new ToastContentBuilder()
                    .AddText("Download Complete")
                    .AddText($"\"{fileName}\" has been saved.")
                    .AddAttributionText("YT Downloader Pro")
                    .Show();
            }
            catch
            {
                // Fallback to old-style Balloon Tip if Toast fails
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

            // Clear any lingering toast history
            try { ToastNotificationManagerCompat.History.Clear(); } catch { }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
    }
}
