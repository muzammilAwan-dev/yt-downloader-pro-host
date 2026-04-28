using System;
using System.IO;
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
        private FileSystemWatcher? _payloadWatcher;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Log("=====================================");
            AppLogger.Log("=== YTDLP HOST APPLICATION START ===");
            AppLogger.Log($"OS Version: {Environment.OSVersion}");

            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            this.DispatcherUnhandledException += (s, args) =>
            {
                AppLogger.Log($"[CRITICAL UI CRASH] {args.Exception.Message}\n{args.Exception.StackTrace}");
                System.Windows.MessageBox.Show($"UI Thread Crash Prevented:\n\n{args.Exception.Message}", "Fatal Error Caught", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; 
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    AppLogger.Log($"[CRITICAL BACKGROUND CRASH] {ex.Message}\n{ex.StackTrace}");
                    System.Windows.MessageBox.Show($"Background Thread Crash:\n\n{ex.Message}", "Fatal Error Caught", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            string? urlArg = e.Args.FirstOrDefault();
            if (!string.IsNullOrEmpty(urlArg)) AppLogger.Log($"[BOOT] Launch Arguments Received: {urlArg}");
            else AppLogger.Log("[BOOT] Launched normally (no protocol arguments).");

            base.OnStartup(e);

            if (!ProtocolHandler.IsRegistered())
            {
                AppLogger.Log("[BOOT] Registering URL Protocol.");
                ProtocolHandler.Register();
            }

            string payloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YT Downloader Pro", "Payloads");
            if (!Directory.Exists(payloadsDir)) Directory.CreateDirectory(payloadsDir);

            _singleInstanceManager = new SingleInstanceManager();
            var isFirstInstance = _singleInstanceManager.Initialize();

            if (!isFirstInstance)
            {
                AppLogger.Log("[BOOT] Secondary instance detected. Routing payload...");
                var url = urlArg ?? "ytdlp://show";
                
                if (url != "ytdlp://show")
                {
                    string payloadFile = Path.Combine(payloadsDir, Guid.NewGuid().ToString() + ".url");
                    File.WriteAllText(payloadFile, url);
                    AppLogger.Log("[BOOT] Payload saved to disk queue.");
                }
                
                // WARNING FIX: Explicit discard '_ =' tells compiler we intentionally aren't waiting
                _ = Task.Run(async () => 
                {
                    try 
                    { 
                        await SingleInstanceManager.SendUrlToRunningInstanceAsync("ytdlp://show"); 
                        await Task.Delay(500); 
                    } 
                    catch { }
                    finally { Environment.Exit(0); }
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

            _payloadWatcher = new FileSystemWatcher(payloadsDir, "*.url") { EnableRaisingEvents = true };
            _payloadWatcher.Created += (s, args) => ProcessPayloadFile(args.FullPath);

            foreach (var file in Directory.GetFiles(payloadsDir, "*.url")) ProcessPayloadFile(file);

            if (!string.IsNullOrEmpty(urlArg) && urlArg.StartsWith("ytdlp://"))
            {
                _mainViewModel.ProcessUrl(urlArg);
            }
        }

        private void ProcessPayloadFile(string filePath)
        {
            // WARNING FIX: Explicit discard '_ ='
            _ = Task.Run(async () => 
            {
                try 
                {
                    await Task.Delay(100); 
                    string url = File.ReadAllText(filePath);
                    File.Delete(filePath);
                    
                    AppLogger.Log($"[FILE IPC] Primary instance successfully extracted payload from disk.");
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (url != "ytdlp://show" && url.StartsWith("ytdlp://")) _mainViewModel?.ProcessUrl(url);
                        ShowMainWindow();
                    });
                }
                catch (Exception ex) { AppLogger.Log($"[FILE IPC ERROR] {ex.Message}"); }
            });
        }

        private void OnUrlReceived(object? sender, string url)
        {
            AppLogger.Log($"[NAMED PIPE] Primary instance woke up via pipe.");
            Dispatcher.BeginInvoke(() =>
            {
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
            
            // WARNING FIX: Safely check for null before accessing properties
            if (_mainViewModel != null) 
            {
                _mainViewModel.IsWindowVisible = false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _payloadWatcher?.Dispose();
            _mainViewModel?.Dispose();
            _singleInstanceManager?.Dispose();
            base.OnExit(e);
        }
    }
}
