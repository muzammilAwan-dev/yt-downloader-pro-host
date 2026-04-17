using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost
{
    public partial class App : System.Windows.Application
    {
        private SingleInstanceManager? _singleInstanceManager;
        private MainViewModel? _mainViewModel;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Check if yt-dlp is available
            CheckYtDlpPresence();

            // Register protocol handler if needed
            if (!ProtocolHandler.IsRegistered())
            {
                ProtocolHandler.Register();
            }

            // Initialize single instance manager
            _singleInstanceManager = new SingleInstanceManager();
            var isFirstInstance = _singleInstanceManager.Initialize();

            if (!isFirstInstance)
            {
                // Forward URL to running instance and exit
                var url = e.Args.FirstOrDefault();
                if (!string.IsNullOrEmpty(url))
                {
                    // FIXED: Block and Wait for the pipe to send the message before shutting down
                    SingleInstanceManager.SendUrlToRunningInstanceAsync(url).Wait(3000);
                }
                else
                {
                    // FIXED: Block and Wait for the pipe to send the message before shutting down
                    SingleInstanceManager.SendUrlToRunningInstanceAsync("ytdlp://show").Wait(3000);
                }

                _singleInstanceManager.Dispose();
                Shutdown(0);
                return;
            }

            // First instance - set up URL received handler
            _singleInstanceManager.UrlReceived += OnUrlReceived;

            // Create ViewModel
            _mainViewModel = new MainViewModel();
            _mainViewModel.RequestShowWindow += OnRequestShowWindow;

            // Create main window
            _mainWindow = new MainWindow { DataContext = _mainViewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.StateChanged += OnMainWindowStateChanged;

            // Show window initially
            _mainWindow.Show();
            _mainWindow.Activate();

            // Process command line URL if provided
            var startupUrl = e.Args.FirstOrDefault();
            if (!string.IsNullOrEmpty(startupUrl) && startupUrl.StartsWith("ytdlp://"))
            {
                _mainViewModel.ProcessUrl(startupUrl);
            }
        }

        private void OnUrlReceived(object? sender, string url)
        {
            if (_mainViewModel == null) return;

            if (url == "ytdlp://show")
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ShowMainWindow();
                });
            }
            else if (url.StartsWith("ytdlp://"))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _mainViewModel.ProcessUrl(url);
                    ShowMainWindow();
                });
            }
        }

        private void OnRequestShowWindow(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(ShowMainWindow);
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;

            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel the close and minimize to tray instead
            e.Cancel = true;
            _mainWindow?.Hide();
            _mainViewModel!.IsWindowVisible = false;
        }

        private void OnMainWindowStateChanged(object? sender, EventArgs e)
        {
            if (_mainWindow?.WindowState == WindowState.Minimized)
            {
                _mainWindow.Hide();
                _mainViewModel!.IsWindowVisible = false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainViewModel?.Dispose();
            _singleInstanceManager?.Dispose();
            base.OnExit(e);
        }

        private static void CheckYtDlpPresence()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "yt-dlp.exe",
                    Arguments = "--version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return;
                proc.WaitForExit(5000);

                if (proc.ExitCode != 0)
                {
                    // FIXED: Added System.Windows explicitly to resolve the ambiguous reference
                    System.Windows.MessageBox.Show(
                        "yt-dlp.exe was found but returned an error.\n\n" +
                        "Please ensure yt-dlp is correctly installed.",
                        "YT Downloader Pro - Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // yt-dlp not found - will show dialog in MainViewModel
            }
        }
    }
}
