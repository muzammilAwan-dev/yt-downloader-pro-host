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
            // HARDWARE ACCELERATION FIX: Force CPU rendering to prevent GPU driver crashes under heavy UI load
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // 1. BULLETPROOFING: Catch all UI Thread Exceptions
            this.DispatcherUnhandledException += (s, args) =>
            {
                System.Windows.MessageBox.Show($"UI Thread Crash Prevented:\n\n{args.Exception.Message}", "Fatal Error Caught", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; 
            };

            // 2. BULLETPROOFING: Catch all Background Thread Exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    System.Windows.MessageBox.Show($"Background Thread Crash:\n\n{ex.Message}", "Fatal Error Caught", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            base.OnStartup(e);

            if (!ProtocolHandler.IsRegistered())
                ProtocolHandler.Register();

            _singleInstanceManager = new SingleInstanceManager();
            var isFirstInstance = _singleInstanceManager.Initialize();

            if (!isFirstInstance)
            {
                var url = e.Args.FirstOrDefault() ?? "ytdlp://show";
                
                // 3. DEADLOCK FIX: Never use .Wait() on the UI thread. Use background tasks.
                Task.Run(async () => 
                {
                    await SingleInstanceManager.SendUrlToRunningInstanceAsync(url);
                    Environment.Exit(0);
                });
                return;
            }

            _singleInstanceManager.UrlReceived += OnUrlReceived;
            _mainViewModel = new MainViewModel();
            _mainViewModel.RequestShowWindow += OnRequestShowWindow;

            _mainWindow = new MainWindow { DataContext = _mainViewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            
            // [FIX APPLIED] Removed the StateChanged event hook here so standard minimize goes to the Taskbar

            _mainWindow.Show();
            _mainWindow.Activate();

            var startupUrl = e.Args.FirstOrDefault();
            if (!string.IsNullOrEmpty(startupUrl) && startupUrl.StartsWith("ytdlp://"))
            {
                _mainViewModel.ProcessUrl(startupUrl);
            }
        }

        private void OnUrlReceived(object? sender, string url)
        {
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

        // [FIX APPLIED] Removed the OnMainWindowStateChanged method completely

        protected override void OnExit(ExitEventArgs e)
        {
            _mainViewModel?.Dispose();
            _singleInstanceManager?.Dispose();
            base.OnExit(e);
        }
    }
}
