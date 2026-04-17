using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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
        private YtDlpRunner? _currentRunner;
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
        public IRelayCommand<DownloadItemViewModel> CancelDownloadCommand { get; }
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
            CancelDownloadCommand = new RelayCommand<DownloadItemViewModel>(CancelDownload, CanCancel);
            RemoveDownloadCommand = new RelayCommand<DownloadItemViewModel>(RemoveDownload, CanRemove);
            OpenFolderCommand = new RelayCommand<DownloadItemViewModel>(OpenFolder, _ => true);
            PlayFileCommand = new RelayCommand<DownloadItemViewModel>(PlayFile, _ => true);
            ClearCompletedCommand = new RelayCommand(ClearCompleted);
            ShowWindowCommand = new RelayCommand(() => RequestShowWindow?.Invoke(this, EventArgs.Empty));
            ExitCommand = new RelayCommand(ExitApplication);
            MinimizeToTrayCommand = new RelayCommand(() => IsWindowVisible = false);

            _downloads.CollectionChanged += (s, e) =>
            {
                HasDownloads = _downloads.Count > 0;
                HasCompletedDownloads = _downloads.Any(d => d.IsCompleted);
                UpdateActiveCount();
            };

            CheckYtDlpExists();
        }

        private void CheckYtDlpExists()
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
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                }
            }
            catch
            {
                StatusText = "yt-dlp not found. Please run setup.bat first.";
                System.Windows.MessageBox.Show(
                    "yt-dlp.exe was not found in your PATH.\n\n" +
                    "Please run setup.bat first to install yt-dlp, ffmpeg, and configure the protocol handler.",
                    "YT Downloader Pro - Component Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void ProcessUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                if (!url.StartsWith("ytdlp://", StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = "Invalid protocol URL received.";
                    return;
                }

                var payload = url.Substring(8).TrimEnd('/');
                var parts = payload.Split(new[] { "||" }, StringSplitOptions.None);

                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    StatusText = "Invalid download link received.";
                    return;
                }

                var command = DecodeBase64(parts[0]);
                if (string.IsNullOrWhiteSpace(command))
                {
                    StatusText = "Invalid download link received.";
                    return;
                }

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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cookie write failed: {ex.Message}");
                    }
                }

                var resolution = ExtractResolution(command);

                var task = new DownloadTask
                {
                    UrlPayload = url,
                    Command = command,
                    CookiePayload = cookieContent,
                    CookieFilePath = cookieFilePath ?? string.Empty,
                    Resolution = resolution,
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
            catch (FormatException)
            {
                StatusText = "Invalid download link received (bad Base64).";
            }
            catch (Exception ex)
            {
                StatusText = $"Error processing URL: {ex.Message}";
            }
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;

            try
            {
                while (!_disposed)
                {
                    var next = _downloads.FirstOrDefault(d => d.Task.Status == DownloadStatus.Queued);
                    if (next == null) break;

                    next.Task.Status = DownloadStatus.Downloading;
                    next.Refresh();
                    SelectedItem = next;
                    RequestScrollToItem?.Invoke(this, next);

                    StatusText = $"Downloading: {next.DisplayTitle}";
                    UpdateActiveCount();

                    _currentRunner = new YtDlpRunner();
                    _currentRunner.OnProgressUpdate += OnRunnerProgress;
                    _currentRunner.OnDownloadComplete += OnRunnerComplete;
                    _currentRunner.OnDownloadError += OnRunnerError;
                    _currentRunner.OnInfoExtracted += OnRunnerInfo;

                    await _currentRunner.ExecuteAsync(next.Task);

                    _currentRunner.Dispose();
                    _currentRunner = null;

                    UpdateActiveCount();
                }

                var remaining = _downloads.Count(d => d.Task.Status == DownloadStatus.Downloading || d.Task.Status == DownloadStatus.Queued);
                if (remaining == 0)
                {
                    StatusText = "All downloads complete";
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        private void OnRunnerProgress(object? sender, ProgressEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    vm.Refresh();
                    UpdateActiveCount();
                });
            }
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

                    _ = DelayedRemoveAsync(vm);
                });
            }
        }

        private void OnRunnerError(object? sender, DownloadErrorEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    vm.Refresh();
                    UpdateActiveCount();
                });
            }
        }

        private void OnRunnerInfo(object? sender, ExtractedInfoEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => vm.Refresh());
            }
        }

        private async Task DelayedRemoveAsync(DownloadItemViewModel vm)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (vm.Task.Status == DownloadStatus.Completed && _downloads.Contains(vm))
                {
                    _downloads.Remove(vm);
                    if (!string.IsNullOrEmpty(vm.Task.CookieFilePath) && File.Exists(vm.Task.CookieFilePath))
                    {
                        try { File.Delete(vm.Task.CookieFilePath); } catch { }
                    }
                }
            });
        }

        private void CancelDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;

            if (vm.Task.Status == DownloadStatus.Downloading && _currentRunner != null)
            {
                _currentRunner.Cancel();
            }

            vm.Task.Status = DownloadStatus.Cancelled;
            vm.Refresh();
            UpdateActiveCount();

            CleanupCookieFile(vm.Task);
        }

        private void RemoveDownload(DownloadItemViewModel? vm)
        {
            if (vm == null) return;

            if (vm.Task.Status == DownloadStatus.Downloading && _currentRunner != null)
            {
                _currentRunner.Cancel();
            }

            CleanupCookieFile(vm.Task);
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private static void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task?.OutputPath == null) return;

            var path = vm.Task.OutputPath;
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
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
            if (vm?.Task?.OutputPath == null) return;

            var path = vm.Task.OutputPath;
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
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
            {
                try { File.Delete(task.CookieFilePath); } catch { }
            }
        }

        private void UpdateActiveCount()
        {
            ActiveDownloadCount = _downloads.Count(d => d.Task.Status == DownloadStatus.Queued || d.Task.Status == DownloadStatus.Downloading);
            _trayService.UpdateTooltip(ActiveDownloadCount == 0
                ? "YT Downloader Pro - Idle"
                : $"YT Downloader Pro - {ActiveDownloadCount} active");
        }

        private static bool CanCancel(DownloadItemViewModel? vm) => vm?.IsCancellable ?? false;
        private static bool CanRemove(DownloadItemViewModel? vm)
        {
            if (vm == null) return false;
            return vm.Task.Status == DownloadStatus.Completed || vm.Task.Status == DownloadStatus.Error || vm.Task.Status == DownloadStatus.Cancelled;
        }

        private static string DecodeBase64(string input)
        {
            string padded = input.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            
            var bytes = Convert.FromBase64String(padded);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string ExtractResolution(string command)
        {
            if (command.Contains("ba") && (command.Contains("extract-audio") || command.Contains("audio")))
                return "Audio";
            var match = System.Text.RegularExpressions.Regex.Match(command, @"height<=?(\d+)");
            if (match.Success) return match.Groups[1].Value + "p";
            match = System.Text.RegularExpressions.Regex.Match(command, @"(\d+)p");
            if (match.Success) return match.Groups[1].Value + "p";
            return "";
        }

        private static string ExtractTitleHint(string command)
        {
            var match = System.Text.RegularExpressions.Regex.Match(command, @"-o\s+""([^""]+)""");
            if (match.Success)
            {
                var template = match.Groups[1].Value;
                return Path.GetFileNameWithoutExtension(template)
                    .Replace("%(title)s", "YouTube Video")
                    .Replace("%(uploader)s", "Channel");
            }
            return "YouTube Video";
        }

        private static string ExtractSaveDirectory(string command)
        {
            var match = System.Text.RegularExpressions.Regex.Match(command, @"-o\s+""([^""]+)""");
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
            foreach (var vm in _downloads)
            {
                CleanupCookieFile(vm.Task);
            }

            _currentRunner?.Cancel();
            _currentRunner?.Dispose();

            System.Windows.Application.Current?.Shutdown();
        }

        public void ShowTrayIcon() => _trayService.SetVisible(true);
        public void HideTrayIcon() => _trayService.SetVisible(false);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _currentRunner?.Cancel();
            _currentRunner?.Dispose();

            foreach (var vm in _downloads)
            {
                CleanupCookieFile(vm.Task);
            }

            _trayService.Dispose();
        }
    }
}
