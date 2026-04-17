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

            if (!ProtocolHandler.IsRegistered())
            {
                ProtocolHandler.Register();
            }

            _singleInstanceManager = new SingleInstanceManager();
            var isFirstInstance = _singleInstanceManager.Initialize();

            if (!isFirstInstance)
            {
                var url = e.Args.FirstOrDefault();
                if (!string.IsNullOrEmpty(url))
                {
                    _ = Task.Run(async () =>
                    {
                        await SingleInstanceManager.SendUrlToRunningInstanceAsync(url);
                    });
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await SingleInstanceManager.SendUrlToRunningInstanceAsync("ytdlp://show");
                    });
                }

                _singleInstanceManager.Dispose();
                Shutdown(0);
                return;
            }

            _singleInstanceManager.UrlReceived += OnUrlReceived;

            _mainViewModel = new MainViewModel();
            _mainViewModel.RequestShowWindow += OnRequestShowWindow;

            _mainWindow = new MainWindow { DataContext = _mainViewModel };
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.StateChanged += OnMainWindowStateChanged;

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
    }
}