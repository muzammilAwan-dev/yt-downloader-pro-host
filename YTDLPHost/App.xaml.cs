using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
            // Initialize application logging.
            AppLogger.Log("=====================================");
            AppLogger.Log("=== YTDLP HOST APPLICATION START ===");
            AppLogger.Log($"OS Version: {Environment.OSVersion}");

            // Configure hardware acceleration: Force CPU rendering to prevent potential GPU driver instability.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // Handle unhandled exceptions on the UI thread.
            this.DispatcherUnhandledException += (s, args) =>
            {
                AppLogger.Log($"[CRITICAL UI CRASH] {args.Exception.Message}\n{args.Exception.StackTrace}");
                System.Windows.MessageBox.Show($"UI Thread Crash Prevented:\n\n{args.Exception.Message}", "Fatal Error Caught", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; 
            };

            // Handle unhandled exceptions on background threads.
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    AppLogger.Log($"[CRITICAL BACKGROUND CRASH] {ex.Message}\n{ex.StackTrace}");
                    System.Windows.MessageBox.Show($"Background Thread Crash:\n\n{ex.Message}", "Fatal Error Caught", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Log launch arguments.
            string? urlArg = e.Args.FirstOrDefault();
            if (!string.IsNullOrEmpty(urlArg))
            {
                AppLogger.Log($"[BOOT] Launch Arguments Received: {urlArg}");
            }
            else
            {
                AppLogger.Log("[BOOT] Launched normally (no protocol arguments).");
            }

            base.OnStartup(e);

            if (!ProtocolHandler.IsRegistered())
            {
                AppLogger.Log("[BOOT] Registering URL Protocol.");
                ProtocolHandler.Register();
            }

            _singleInstanceManager = new SingleInstanceManager();
            var isFirstInstance = _singleInstanceManager.Initialize();

            if (!isFirstInstance)
            {
                AppLogger.Log("[BOOT] Secondary instance detected. Forwarding arguments and exiting.");
                var url = urlArg ?? "ytdlp://show";
                
                // Execute instance forwarding on a background task to prevent UI thread deadlocks.
                Task.Run(async () => 
                {
                    await SingleInstanceManager.SendUrlToRunningInstanceAsync(url);
                    Environment.Exit(0);
                });
                return;
            }

            AppLogger.Log("[BOOT] Primary instance established. Initializing UI components.");
            _singleInstanceManager.UrlReceived += OnUrlReceived;
            _mainViewModel = new MainViewModel();
            _mainViewModel.RequestShowWindow += OnRequestShowWindow;

            _mainWindow = new MainWindow { DataContext = _mainViewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            
            _mainWindow.Show();
            _mainWindow.Activate();

            if (!string.IsNullOrEmpty(urlArg) && urlArg.StartsWith("ytdlp://"))
            {
                _mainViewModel.ProcessUrl(urlArg);
            }
        }

        private void OnUrlReceived(object? sender, string url)
        {
            AppLogger.Log($"[NAMED PIPE] Received argument from secondary instance: {url}");
            if (_mainViewModel == null) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (url != "ytdlp://show" && url.StartsWith("ytdlp://"))
                    _mainViewModel.ProcessUrl(url);
                ShowMainWindow();
            });
        }

        private void OnRequestShowWindow(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ShowMainWindow);

        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            _mainWindow?.Hide();
            _mainViewModel!.IsWindowVisible = false;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppLogger.Log("=== APPLICATION EXITING ===");
            AppLogger.Log("=====================================\n");
            _mainViewModel?.Dispose();
            _singleInstanceManager?.Dispose();
            base.OnExit(e);
        }
    }
}
