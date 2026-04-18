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
            
            MinimizeToTrayCommand = new RelayCommand(() => 
            {
                IsWindowVisible = false;
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.WindowState = WindowState.Minimized;
                }
            });

            _downloads.CollectionChanged += (s, e) =>
            {
                HasDownloads = _downloads.Count > 0;
                HasCompletedDownloads = _downloads.Any(d => d.IsCompleted);
                UpdateActiveCount();
            };

            CheckYtDlpExists();

            // [FIX APPLIED] SILENT ENGINE UPDATE: Keeps yt-dlp healthy without bothering the user
            _ = Task.Run(() => {
                try 
                { 
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo 
                    { 
                        FileName = "yt-dlp.exe", 
                        Arguments = "-U", 
                        CreateNoWindow = true, 
                        UseShellExecute = false 
                    }); 
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
                    System.Windows.MessageBox.Show(
                        "yt-dlp.exe was not found in your PATH.\n\n" +
                        "Please run setup.bat first to install yt-dlp, ffmpeg, and configure the protocol handler.",
                        "YT Downloader Pro - Component Missing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
        }

        private bool IsCommandSafe(string command)
        {
            string[] forbiddenFlags = { "--exec", "--exec-before-download", "--postprocessor-args", "--setup-hook" };
            foreach (var flag in forbiddenFlags)
            {
                if (command.Contains(flag, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"SECURITY ALERT: Blocked command injection flag: {flag}");
                    return false;
                }
            }

            var match = Regex.Match(command, @"-(?:o|P)\s+""([^""]+)""");
            if (match.Success)
            {
                string path = match.Groups[1].Value;
                if (path.Contains("..\\") || path.Contains("../") || 
                    path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains(@"\Start Menu\Programs\Startup", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"SECURITY ALERT: Blocked malicious path traversal: {path}");
                    return false;
                }
            }

            return true;
        }

        public void ProcessUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                url = Uri.UnescapeDataString(url);

                if (!url.StartsWith("ytdlp://", StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = "Invalid protocol URL received.";
                    return;
                }

                var payload = url.Substring(8).TrimEnd('/');
                var parts = payload.Split(new[] { "||" }, StringSplitOptions.None);

                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return;

                var command = DecodeBase64(parts[0]);
                if (string.IsNullOrWhiteSpace(command)) return;

                if (!IsCommandSafe(command))
                {
                    StatusText = "Security Error: Blocked potentially malicious payload.";
                    System.Windows.MessageBox.Show("A potentially unsafe download command was blocked for your security.", "YT Downloader Pro - Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // [FIX APPLIED] GUI SAFETY: Force --progress so the UI progress bar never breaks on custom commands
                if (!command.Contains("--progress")) command += " --progress";

                string? cookieContent = null;
                string? cookieFilePath = null;

                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    try
                    {
                        cookieContent = DecodeBase64(parts[1]);
                        if (!string.IsNullOrWhiteSpace(cookieContent))
                        {
                            var cookieFile = Path.Combine(Path.GetTempPath(), $"ytdlp_cookies_{Guid.NewGuid()}.txt");
                            File.WriteAllText(cookieFile, cookieContent, Encoding.UTF8);
                            cookieFilePath = cookieFile;
                        }
                    }
                    catch { }
                }

                var task = new DownloadTask
                {
                    UrlPayload = url,
                    Command = command,
                    CookiePayload = cookieContent,
                    CookieFilePath = cookieFilePath ?? string.Empty,
                    Resolution = ExtractResolution(command),
                    Title = ExtractTitleHint(command),
                    Status = DownloadStatus.Queued
                };

                var vm = new DownloadItemViewModel(task);
                _downloads.Add(vm);
                HasDownloads = true;

                StatusText = $"Added: {vm.DisplayTitle}";
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
                    SelectedItem = next;
                    RequestScrollToItem?.Invoke(this, next);

                    StatusText = $"Downloading: {next.DisplayTitle}";
                    UpdateActiveCount();

                    _ = StartDownloadTaskAsync(next);

                    await Task.Delay(1500); 
                }

                if (_activeRunners.Count == 0 && _downloads.Count(d => d.Task.Status == DownloadStatus.Queued) == 0)
                {
                    StatusText = "All downloads complete";
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

            runner.OnProgressUpdate += OnRunnerProgress;
            runner.OnDownloadComplete += OnRunnerComplete;
            runner.OnDownloadError += OnRunnerError;
            runner.OnInfoExtracted += OnRunnerInfo;

            await runner.ExecuteAsync(vm.Task);

            runner.Dispose();
            _activeRunners.Remove(vm.Id);

            UpdateActiveCount();
            _ = ProcessQueueAsync();
        }

        private void OnRunnerProgress(object? sender, ProgressEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { vm.Refresh(); UpdateActiveCount(); });
        }

        private void OnRunnerComplete(object? sender, CompleteEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    vm.Refresh();
                    HasCompletedDownloads = true;
                    UpdateActiveCount();
                    _trayService.ShowDownloadCompleteNotification("Download Complete", e.Title);
                    
                    Task.Run(() => CleanupPartialFiles(vm.Task));
                });
            }
        }

        private void OnRunnerError(object? sender, DownloadErrorEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { vm.Refresh(); UpdateActiveCount(); });
        }

        private void OnRunnerInfo(object? sender, ExtractedInfoEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) 
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => 
                {
                    vm.Refresh();
                    // [FIX APPLIED] FOOTER TITLE SYNC: Updates bottom status bar instantly when title is found
                    if (vm.Task.Status == DownloadStatus.Downloading) StatusText = $"Downloading: {vm.DisplayTitle}";
                });
            }
        }

        private void PauseDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;

            if (_activeRunners.TryGetValue(vm.Id, out var runner))
                runner.Cancel();

            vm.Task.Status = DownloadStatus.Paused;
            vm.Refresh();
            UpdateActiveCount();
        }

        private void CancelDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;

            if (_activeRunners.TryGetValue(vm.Id, out var runner))
                runner.Cancel();

            vm.Task.Status = DownloadStatus.Cancelled;
            vm.Refresh();
            UpdateActiveCount();

            Task.Run(() => CleanupPartialFiles(vm.Task, forceDeleteAll: true));
            CleanupCookieFile(vm.Task);
        }

        private void CleanupPartialFiles(DownloadTask task, bool forceDeleteAll = false)
        {
            foreach (var filePath in task.TrackedFiles.Distinct().ToList())
            {
                if (!forceDeleteAll && filePath.Equals(task.OutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }
            }

            if (forceDeleteAll && !string.IsNullOrEmpty(task.OutputPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(task.OutputPath) ?? "";
                    string title = Path.GetFileNameWithoutExtension(task.OutputPath);

                    if (Directory.Exists(dir) && !string.IsNullOrEmpty(title))
                    {
                        var files = Directory.GetFiles(dir, $"{title}*");
                        foreach (var file in files)
                        {
                            if (file.EndsWith(".part") || file.EndsWith(".ytdl") || file.EndsWith(".frag"))
                            {
                                File.Delete(file);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void ResumeDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;

            vm.Task.Status = DownloadStatus.Queued;
            vm.Task.ErrorMessage = "";
            vm.Refresh();
            UpdateActiveCount();
            _ = ProcessQueueAsync();
        }

        private void RemoveDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;

            if (_activeRunners.TryGetValue(vm.Id, out var runner))
                runner.Cancel();

            CleanupCookieFile(vm.Task);
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private static void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null) return;
            var path = vm.Task.OutputPath;

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (!string.IsNullOrEmpty(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                    return;
                }
            }

            var saveDir = ExtractSaveDirectory(vm.Task.Command);
            if (Directory.Exists(saveDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = saveDir,
                    UseShellExecute = true
                });
            }
        }

        private static void PlayFile(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null) return;
            var path = vm.Task.OutputPath;
            
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return;
            }

            OpenFolder(vm);
        }

        private void ClearCompleted()
        {
            var completed = _downloads.Where(d => d.Task.Status == DownloadStatus.Completed).ToList();
            foreach (var vm in completed)
            {
                CleanupCookieFile(vm.Task);
                _downloads.Remove(vm);
            }
            HasCompletedDownloads = _downloads.Any(d => d.Task.Status == DownloadStatus.Completed);
            UpdateActiveCount();
        }

        private static void CleanupCookieFile(DownloadTask task)
        {
            if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath))
                try { File.Delete(task.CookieFilePath); } catch { }
        }

        private void UpdateActiveCount()
        {
            ActiveDownloadCount = _downloads.Count(d => d.Task.Status == DownloadStatus.Queued || d.Task.Status == DownloadStatus.Downloading);
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
            if (command.Contains("ba") && (command.Contains("extract-audio") || command.Contains("audio"))) return "Audio";
            var match = Regex.Match(command, @"height<=?(\d+)");
            if (match.Success) return match.Groups[1].Value + "p";
            match = Regex.Match(command, @"(\d+)p");
            if (match.Success) return match.Groups[1].Value + "p";
            return "";
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
                var template = match.Groups[1].Value;
                var dir = Path.GetDirectoryName(template);
                if (!string.IsNullOrEmpty(dir))
                {
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (dir.StartsWith("~"))
                        dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), dir.Substring(1).TrimStart('/', '\\'));
                    if (Directory.Exists(dir))
                        return dir;
                }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void ExitApplication()
        {
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            foreach (var runner in _activeRunners.Values) runner.Cancel();
            System.Windows.Application.Current?.Shutdown();
        }

        public void ShowTrayIcon() => _trayService.SetVisible(true);
        public void HideTrayIcon() => _trayService.SetVisible(false);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var runner in _activeRunners.Values) runner.Dispose();
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            _trayService.Dispose();
        }
    }
}
