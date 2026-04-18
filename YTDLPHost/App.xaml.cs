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
            // HARDWARE ACCELERATION FIX: Prevents GPU driver crashes under heavy UI load
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // BULLETPROOFING: UI Thread Exception Handler
            this.DispatcherUnhandledException += (s, args) =>
            {
                System.Windows.MessageBox.Show($"UI Thread Error: {args.Exception.Message}", "YT Downloader Pro - Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; 
            };

            // BULLETPROOFING: Background Thread Exception Handler
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    System.Windows.MessageBox.Show($"Background Thread Error: {ex.Message}", "YT Downloader Pro - Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            base.OnStartup(e);

            // Ensure protocol is registered on launch
            if (!ProtocolHandler.IsRegistered())
                ProtocolHandler.Register();

            _singleInstanceManager = new SingleInstanceManager();
            var isFirstInstance = _singleInstanceManager.Initialize();

            if (!isFirstInstance)
            {
                // Send the URL to the existing instance and shut down this one
                var url = e.Args.FirstOrDefault() ?? "ytdlp://show";
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

            // NOTE: OnMainWindowStateChanged removed to allow standard Taskbar minimization.
            // Only the custom minimize button in MainWindow.xaml now triggers the Tray-hide.

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
            // Restore window if it was minimized to the taskbar
            if (_mainWindow.WindowState == WindowState.Minimized) 
                _mainWindow.WindowState = WindowState.Normal;
            
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainViewModel!.IsWindowVisible = true;
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Intercept close button to hide to tray instead of exiting
            e.Cancel = true;
            _mainWindow?.Hide();
            _mainViewModel!.IsWindowVisible = false;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainViewModel?.Dispose();
            _singleInstanceManager?.Dispose();
            base.OnExit(e);
        }
    }
}
