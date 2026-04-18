using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly TrayIconService _trayService;
        private readonly ObservableCollection<DownloadItemViewModel> _downloads = new();
        private readonly Dictionary<Guid, YtDlpRunner> _activeRunners = new();
        private const int MaxConcurrentDownloads = 3; 

        private bool _isProcessingQueue;
        private bool _disposed;

        [ObservableProperty]
        private bool _isWindowVisible = true;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private int _activeDownloadCount = 0;

        [ObservableProperty]
        private bool _hasDownloads;

        [ObservableProperty]
        private bool _hasCompletedDownloads;

        [ObservableProperty]
        private string _emptyStateText = "No active downloads. Click Download in YouTube to add videos.";

        [ObservableProperty]
        private DownloadItemViewModel? _selectedItem;

        public ObservableCollection<DownloadItemViewModel> Downloads => _downloads;

        // Command Definitions
        public IRelayCommand<string> ProcessUrlCommand { get; }
        public IRelayCommand<DownloadItemViewModel> PauseDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> CancelDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> ResumeDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> RemoveDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> OpenFolderCommand { get; }
        public IRelayCommand<DownloadItemViewModel> PlayFileCommand { get; }
        public IRelayCommand ClearCompletedCommand { get; }
        public IRelayCommand ShowWindowCommand { get; }
        public IRelayCommand ExitCommand { get; }
        public IRelayCommand MinimizeToTrayCommand { get; }

        public event EventHandler? RequestShowWindow;
        public event EventHandler<DownloadItemViewModel>? RequestScrollToItem;

        public MainViewModel()
        {
            _trayService = new TrayIconService();
            _trayService.ShowWindowRequested += (s, e) => RequestShowWindow?.Invoke(this, EventArgs.Empty);
            _trayService.ExitRequested += (s, e) => ExitCommand.Execute(null);
            _trayService.Initialize();

            // Initialize RelayCommands
            ProcessUrlCommand = new RelayCommand<string>(ProcessUrl);
            PauseDownloadCommand = new RelayCommand<DownloadItemViewModel>(PauseDownload);
            CancelDownloadCommand = new RelayCommand<DownloadItemViewModel>(CancelDownload);
            ResumeDownloadCommand = new RelayCommand<DownloadItemViewModel>(ResumeDownload);
            RemoveDownloadCommand = new RelayCommand<DownloadItemViewModel>(RemoveDownload);
            OpenFolderCommand = new RelayCommand<DownloadItemViewModel>(OpenFolder);
            PlayFileCommand = new RelayCommand<DownloadItemViewModel>(PlayFile);
            
            ClearCompletedCommand = new RelayCommand(ClearCompleted);
            ShowWindowCommand = new RelayCommand(() => RequestShowWindow?.Invoke(this, EventArgs.Empty));
            ExitCommand = new RelayCommand(ExitApplication);
            
            // Minimize logic: hides window but keeps process alive for tray
            MinimizeToTrayCommand = new RelayCommand(() => 
            {
                IsWindowVisible = false;
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.Hide();
                }
            });

            _downloads.CollectionChanged += (s, e) =>
            {
                HasDownloads = _downloads.Count > 0;
                HasCompletedDownloads = _downloads.Any(d => d.IsCompleted);
                UpdateActiveCount();
            };

            CheckYtDlpExists();

            // SILENT UPDATE: Keeps the yt-dlp engine healthy in the background
            _ = Task.Run(() => {
                try 
                { 
                    var psi = new System.Diagnostics.ProcessStartInfo 
                    { 
                        FileName = "yt-dlp.exe", 
                        Arguments = "-U", 
                        CreateNoWindow = true, 
                        UseShellExecute = false 
                    };
                    System.Diagnostics.Process.Start(psi); 
                } catch { }
            });
        }

        private async void CheckYtDlpExists()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "yt-dlp.exe",
                    Arguments = "--version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                await Task.Run(() => 
                {
                    using var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit(5000);
                });
            }
            catch
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    StatusText = "yt-dlp not found. Please run setup.bat first.";
                });
            }
        }

        private bool IsCommandSafe(string command)
        {
            // Block dangerous command injection flags
            string[] forbiddenFlags = { "--exec", "--exec-before-download", "--postprocessor-args", "--setup-hook" };
            foreach (var flag in forbiddenFlags)
            {
                if (command.Contains(flag, StringComparison.OrdinalIgnoreCase)) return false;
            }

            // Block malicious path traversal
            var match = Regex.Match(command, @"-(?:o|P)\s+""([^""]+)""");
            if (match.Success)
            {
                string path = match.Groups[1].Value;
                if (path.Contains("..\\") || path.Contains("../") || 
                    path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        public void ProcessUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                url = Uri.UnescapeDataString(url);

                if (!url.StartsWith("ytdlp://", StringComparison.OrdinalIgnoreCase)) return;

                var payload = url.Substring(8).TrimEnd('/');
                var parts = payload.Split(new[] { "||" }, StringSplitOptions.None);

                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return;

                var command = DecodeBase64(parts[0]);
                if (string.IsNullOrWhiteSpace(command) || !IsCommandSafe(command)) return;

                // GUI SAFETY: Ensure --progress is always present so we can parse percentages
                if (!command.Contains("--progress")) command += " --progress";

                string? cookieFilePath = null;
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    try
                    {
                        var content = DecodeBase64(parts[1]);
                        var cookieFile = Path.Combine(Path.GetTempPath(), $"ytdlp_cookies_{Guid.NewGuid()}.txt");
                        File.WriteAllText(cookieFile, content, Encoding.UTF8);
                        cookieFilePath = cookieFile;
                    }
                    catch { }
                }

                var task = new DownloadTask
                {
                    UrlPayload = url,
                    Command = command,
                    CookieFilePath = cookieFilePath ?? string.Empty,
                    Resolution = ExtractResolution(command),
                    Title = ExtractTitleHint(command),
                    Status = DownloadStatus.Queued
                };

                var vm = new DownloadItemViewModel(task);
                _downloads.Add(vm);
                
                UpdateActiveCount();
                _ = ProcessQueueAsync();
            }
            catch { }
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;

            try
            {
                while (!_disposed)
                {
                    if (_activeRunners.Count >= MaxConcurrentDownloads) break;

                    var next = _downloads.FirstOrDefault(d => d.Task.Status == DownloadStatus.Queued);
                    if (next == null) break;

                    next.Task.Status = DownloadStatus.Downloading;
                    next.Refresh();
                    
                    StatusText = $"Downloading: {next.DisplayTitle}";
                    UpdateActiveCount();

                    _ = StartDownloadTaskAsync(next);

                    await Task.Delay(1500); // Staggered start for stability
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        private async Task StartDownloadTaskAsync(DownloadItemViewModel vm)
        {
            var runner = new YtDlpRunner();
            _activeRunners[vm.Id] = runner;

            runner.OnProgressUpdate += (s, e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { vm.Refresh(); UpdateActiveCount(); });
            runner.OnDownloadComplete += (s, e) => 
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    vm.Refresh();
                    UpdateActiveCount();
                    _trayService.ShowDownloadCompleteNotification("Download Complete", e.Title);
                    Task.Run(() => CleanupPartialFiles(vm.Task));
                });
            };
            runner.OnDownloadError += (s, e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => vm.Refresh());
            runner.OnInfoExtracted += OnRunnerInfo;

            await runner.ExecuteAsync(vm.Task);

            runner.Dispose();
            _activeRunners.Remove(vm.Id);

            UpdateActiveCount();
            _ = ProcessQueueAsync();
        }

        private void OnRunnerInfo(object? sender, ExtractedInfoEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) 
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => 
                {
                    vm.Refresh();
                    // FOOTER SYNC FIX: Instantly update the bottom status bar with the real title
                    if (vm.Task.Status == DownloadStatus.Downloading)
                    {
                        StatusText = $"Downloading: {vm.DisplayTitle}";
                    }
                });
            }
        }

        private void PauseDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;
            if (_activeRunners.TryGetValue(vm.Id, out var runner)) runner.Cancel();
            vm.Task.Status = DownloadStatus.Paused;
            vm.Refresh();
            UpdateActiveCount();
        }

        private void CancelDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;
            if (_activeRunners.TryGetValue(vm.Id, out var runner)) runner.Cancel();
            vm.Task.Status = DownloadStatus.Cancelled;
            vm.Refresh();
            UpdateActiveCount();
            Task.Run(() => CleanupPartialFiles(vm.Task, forceDeleteAll: true));
        }

        private void CleanupPartialFiles(DownloadTask task, bool forceDeleteAll = false)
        {
            // THREAD-SAFETY FIX: Use Distinct() on the ConcurrentBag for safe cleanup
            foreach (var filePath in task.TrackedFiles.Distinct().ToList())
            {
                if (!forceDeleteAll && filePath.Equals(task.OutputPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(filePath)) try { File.Delete(filePath); } catch { }
            }
        }

        private void ResumeDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;
            vm.Task.Status = DownloadStatus.Queued;
            vm.Refresh();
            _ = ProcessQueueAsync();
        }

        private void RemoveDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;
            if (_activeRunners.TryGetValue(vm.Id, out var runner)) runner.Cancel();
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private static void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null) return;
            var path = vm.Task.OutputPath;
            if (string.IsNullOrEmpty(path)) path = ExtractSaveDirectory(vm.Task.Command);

            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
        }

        private static void PlayFile(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || !File.Exists(vm.Task.OutputPath)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = vm.Task.OutputPath,
                UseShellExecute = true
            });
        }

        private void ClearCompleted()
        {
            var completed = _downloads.Where(d => d.Task.Status == DownloadStatus.Completed).ToList();
            foreach (var vm in completed) _downloads.Remove(vm);
        }

        private void UpdateActiveCount()
        {
            ActiveDownloadCount = _downloads.Count(d => d.IsActive);
            _trayService.UpdateTooltip(ActiveDownloadCount == 0 ? "YT Downloader Pro - Idle" : $"YT Downloader Pro - {ActiveDownloadCount} active");
        }

        private static string DecodeBase64(string input)
        {
            string padded = input.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        private static string ExtractResolution(string command)
        {
            var match = Regex.Match(command, @"(\d+p|audio|ba)");
            return match.Success ? match.Value : "";
        }

        private static string ExtractTitleHint(string command)
        {
            var match = Regex.Match(command, @"-o\s+""([^""]+)""");
            if (match.Success)
            {
                return Path.GetFileNameWithoutExtension(match.Groups[1].Value)
                    .Replace("%(title)s", "Fetching Title...").Replace("%(uploader)s", "Channel");
            }
            return "Fetching Title...";
        }

        private static string ExtractSaveDirectory(string command)
        {
            var match = Regex.Match(command, @"-o\s+""([^""]+)""");
            if (match.Success)
            {
                var dir = Path.GetDirectoryName(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(dir)) return Environment.ExpandEnvironmentVariables(dir);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private void ExitApplication()
        {
            foreach (var runner in _activeRunners.Values) runner.Cancel();
            System.Windows.Application.Current?.Shutdown();
        }

        public void ShowTrayIcon() => _trayService.SetVisible(true);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var runner in _activeRunners.Values) runner.Dispose();
            _trayService.Dispose();
        }
    }
}
