using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly Regex CommandPathRegex = new(@"-(?:o|P)\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex OutputTemplateRegex = new(@"-o\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex ResHeightRegex = new(@"height<=?(\d+)", RegexOptions.Compiled);
        private static readonly Regex ResRegex = new(@"(\d+)p", RegexOptions.Compiled);

        private readonly TrayIconService _trayService;
        private readonly ObservableCollection<DownloadItemViewModel> _downloads = new();
        private readonly Dictionary<Guid, YtDlpRunner> _activeRunners = new();
        
        private const int MaxConcurrentDownloads = 3; 

        private bool _isProcessingQueue;
        private bool _disposed;
        private bool _isDependenciesReady;
        private bool _hasDependencyError;

        [ObservableProperty] private bool _isWindowVisible = true;
        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private int _activeDownloadCount = 0;
        [ObservableProperty] private bool _hasDownloads;
        [ObservableProperty] private bool _hasCompletedDownloads;
        [ObservableProperty] private string _emptyStateText = "No active downloads. Click Download in YouTube to add videos.";
        [ObservableProperty] private DownloadItemViewModel? _selectedItem;

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
            AppLogger.Log("[VM] Initializing MainViewModel...");
            
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

            _ = CheckAndDownloadDependenciesAsync();
        }

        private async Task CheckAndDownloadDependenciesAsync()
        {
            DownloadItemViewModel? setupVm = null;
            
            try
            {
                // FIX: Restored the Engine to LocalAppData to bypass Admin locks, but without complex path injections
                string engineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YT Downloader Pro", "Engine");
                
                if (!Directory.Exists(engineDir)) 
                {
                    Directory.CreateDirectory(engineDir);
                }

                string ytdlpPath = Path.Combine(engineDir, "yt-dlp.exe");
                string ffmpegPath = Path.Combine(engineDir, "ffmpeg.exe");

                if (File.Exists(ytdlpPath) && File.Exists(ffmpegPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Core dependencies located securely in LocalAppData.");
                    _isDependenciesReady = true;
                    _ = Task.Run(() => UpdateYtDlp(ytdlpPath));
                    _ = ProcessQueueAsync(); 
                    return;
                }

                AppLogger.Log("[DEPENDENCIES] Dependencies missing. Creating UX Setup Card.");
                
                var setupTask = new DownloadTask
                {
                    Id = Guid.NewGuid(),
                    Title = "Initial System Setup",
                    Status = DownloadStatus.Downloading,
                    CurrentPhase = "Initializing download...",
                    IsIndeterminate = true
                };
                setupVm = new DownloadItemViewModel(setupTask);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _downloads.Insert(0, setupVm);
                    StatusText = "Downloading required engine updates... Please wait.";
                });

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(20); 
                client.DefaultRequestHeaders.Add("User-Agent", "YTDownloaderPro/6.0 (Windows NT 10.0; Win64; x64)");
                
                if (!File.Exists(ytdlpPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Downloading yt-dlp binary.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => { setupVm.Task.CurrentPhase = "Downloading yt-dlp engine..."; setupVm.Refresh(); });
                    
                    var ytdlpBytes = await client.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                    await File.WriteAllBytesAsync(ytdlpPath, ytdlpBytes);
                }

                if (!File.Exists(ffmpegPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Downloading FFmpeg build archive.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => { setupVm.Task.CurrentPhase = "Downloading FFmpeg media codecs (This may take a minute)..."; setupVm.Refresh(); });

                    string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
                    string extractPath = Path.Combine(Path.GetTempPath(), "ffmpeg_ext");
                    
                    var ffmpegBytes = await client.GetByteArrayAsync("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip");
                    await File.WriteAllBytesAsync(zipPath, ffmpegBytes);
                    
                    AppLogger.Log("[DEPENDENCIES] Extracting FFmpeg archive contents.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => { setupVm.Task.CurrentPhase = "Extracting codecs..."; setupVm.Refresh(); });

                    if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                    ZipFile.ExtractToDirectory(zipPath, extractPath);
                    
                    var extFiles = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories);
                    foreach (var file in extFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) || fileName.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, Path.Combine(engineDir, fileName), true);
                        }
                    }
                    
                    File.Delete(zipPath);
                    Directory.Delete(extractPath, true);
                }

                AppLogger.Log("[DEPENDENCIES] Dependency installation complete.");
                
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    setupVm.Task.CurrentPhase = "Setup Complete!";
                    setupVm.Task.Status = DownloadStatus.Completed;
                    setupVm.Task.Progress = 100.0;
                    setupVm.Task.IsIndeterminate = false;
                    setupVm.Refresh();
                    StatusText = "Ready";
                });

                await Task.Delay(2000);
                System.Windows.Application.Current?.Dispatcher.Invoke(() => _downloads.Remove(setupVm));

                _isDependenciesReady = true;
                _ = Task.Run(() => UpdateYtDlp(ytdlpPath));
                _ = ProcessQueueAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Failed to provision dependencies: {ex.Message}");
                _hasDependencyError = true;
                
                System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                {
                    StatusText = "Network Error. Please check your internet connection.";
                    if (setupVm != null)
                    {
                        setupVm.Task.Status = DownloadStatus.Error;
                        setupVm.Task.CurrentPhase = "Setup Failed! Please restart the app.";
                        setupVm.Task.IsIndeterminate = false;
                        setupVm.Task.ErrorMessage = "Check your internet connection and try again.";
                        setupVm.Refresh();
                    }
                });
            }
        }

        private void UpdateYtDlp(string ytdlpPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytdlpPath,
                    Arguments = "-U",
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var outputTask = proc.StandardOutput.ReadToEndAsync();
                    
                    if (proc.WaitForExit(30000))
                    {
                        string output = outputTask.Result;
                        if (output.Contains("up to date", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Log("[DEPENDENCIES] yt-dlp is synchronized with the latest release.");
                        }
                        else if (output.Contains("Updated yt-dlp", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Log("[DEPENDENCIES] yt-dlp successfully patched to the latest version.");
                        }
                    }
                    else
                    {
                        proc.Kill(); 
                        AppLogger.Log("[DEPENDENCIES ERROR] Background update process hung and was terminated.");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Execution of the update sub-process failed: {ex.Message}");
            }
        }

        private bool IsCommandSafe(string command)
        {
            string[] forbiddenFlags = { "--exec", "--exec-before-download", "--postprocessor-args", "--setup-hook" };
            foreach (var flag in forbiddenFlags)
            {
                if (command.Contains(flag, StringComparison.OrdinalIgnoreCase)) return false;
            }

            var match = CommandPathRegex.Match(command);
            if (match.Success)
            {
                string path = match.Groups[1].Value;
                if (path.Contains("..\\") || path.Contains("../") || 
                    path.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains(@"\Start Menu\Programs\Startup", StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        public void ProcessUrl(string? url)
        {
            AppLogger.Log($"https://www.amazon.com/CPU-Processors-Memory-Computer-Add-Ons/b?ie=UTF8&node=229189 Validating incoming URL payload. Length: {url?.Length ?? 0}");
            if (string.IsNullOrWhiteSpace(url)) return;

            if (_hasDependencyError)
            {
                System.Windows.MessageBox.Show("YT Downloader Pro cannot process links because the initial core setup failed.\n\nPlease ensure you have an active internet connection and restart the application.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                url = Uri.UnescapeDataString(url);

                if (!url.StartsWith("ytdlp://", StringComparison.OrdinalIgnoreCase)) return;

                var payload = url.Substring(8).TrimEnd('/');
                var parts = payload.Split(new[] { "||" }, StringSplitOptions.None);

                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return;

                var command = DecodeBase64(parts[0]);
                AppLogger.Log($"https://www.amazon.com/CPU-Processors-Memory-Computer-Add-Ons/b?ie=UTF8&node=229189 Command decoded successfully. Length: {command.Length}");
                
                if (string.IsNullOrWhiteSpace(command)) return;

                if (!IsCommandSafe(command))
                {
                    StatusText = "Security Error: Blocked potentially malicious payload.";
                    System.Windows.MessageBox.Show("A potentially unsafe download command was blocked for your security.", "YT Downloader Pro - Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            cookieContent = cookieContent.TrimStart('\uFEFF');
                            var cookieFile = Path.Combine(Path.GetTempPath(), $"ytdlp_cookies_{Guid.NewGuid()}.txt");
                            File.WriteAllText(cookieFile, cookieContent, new UTF8Encoding(false));
                            cookieFilePath = cookieFile;
                            AppLogger.Log("https://www.amazon.com/CPU-Processors-Memory-Computer-Add-Ons/b?ie=UTF8&node=229189 Session cookies provisioned to local temporary storage (BOM removed).");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"https://www.ibm.com/support/pages/troubleshooting-processor-issues Cookie deserialization failure: {ex.Message}");
                    }
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
                
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _downloads.Add(vm);
                    HasDownloads = true;
                    StatusText = $"Added: {vm.DisplayTitle}";
                });
                
                UpdateActiveCount();
                AppLogger.Log($"[QUEUE] Download task assigned to queue ID: {vm.Id}");
                
                _ = ProcessQueueAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"https://www.reddit.com/r/buildapc/comments/1j7an8k/cpu_is_affected_by_a_critical_intel_chip_bug/ Payload execution fault: {ex.Message}");
            }
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessingQueue) return;
            
            if (!_isDependenciesReady) 
            {
                AppLogger.Log("[QUEUE] Queue processing deferred. Waiting for dependencies to finish downloading.");
                return;
            }

            _isProcessingQueue = true;

            try
            {
                while (!_disposed)
                {
                    if (_activeRunners.Count >= MaxConcurrentDownloads) break;

                    DownloadItemViewModel? next = null;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => next = _downloads.FirstOrDefault(d => d.Task.Status == DownloadStatus.Queued));

                    if (next == null) break;

                    next.Task.Status = DownloadStatus.Downloading;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => { next.Refresh(); SelectedItem = next; StatusText = $"Downloading: {next.DisplayTitle}"; });
                    RequestScrollToItem?.Invoke(this, next);
                    UpdateActiveCount();

                    AppLogger.Log($"[QUEUE] Spawning execution runner for task ID: {next.Id}");
                    _ = StartDownloadTaskAsync(next);

                    await Task.Delay(1500); 
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_activeRunners.Count == 0 && _downloads.Count(d => d.Task.Status == DownloadStatus.Queued) == 0) StatusText = "All downloads complete";
                });
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
            if (vm != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { vm.Refresh(); UpdateActiveCount(); });
        }

        private void OnRunnerComplete(object? sender, CompleteEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
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
            if (vm != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { vm.Refresh(); UpdateActiveCount(); });
        }

        private void OnRunnerInfo(object? sender, ExtractedInfoEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => vm.Refresh());
        }

        private void PauseDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process suspended for task ID: {vm.Id}");
            if (_activeRunners.TryGetValue(vm.Id, out var runner)) runner.Cancel();
            vm.Task.Status = DownloadStatus.Paused;
            vm.Refresh();
            UpdateActiveCount();
        }

        private void CancelDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process termination requested for task ID: {vm.Id}");
            if (_activeRunners.TryGetValue(vm.Id, out var runner)) runner.Cancel();
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
                if (!forceDeleteAll && filePath.Equals(task.OutputPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(filePath)) try { File.Delete(filePath); } catch { }
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
                        foreach (var file in files) if (file.EndsWith(".part") || file.EndsWith(".ytdl") || file.EndsWith(".frag")) File.Delete(file);
                    }
                }
                catch { }
            }
        }

        private void ResumeDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process resumption initiated for task ID: {vm.Id}");
            vm.Task.Status = DownloadStatus.Queued;
            vm.Task.ErrorMessage = "";
            vm.Refresh();
            UpdateActiveCount();
            _ = ProcessQueueAsync();
        }

        private void RemoveDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Task purged from registry. ID: {vm.Id}");
            if (_activeRunners.TryGetValue(vm.Id, out var runner)) runner.Cancel();
            CleanupCookieFile(vm.Task);
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private static void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || vm.Task.Title == "Initial System Setup") return;
            var path = vm.Task.OutputPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true }); return; }
            if (!string.IsNullOrEmpty(path)) { var dir = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true }); return; } }
            var saveDir = ExtractSaveDirectory(vm.Task.Command);
            if (Directory.Exists(saveDir)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = saveDir, UseShellExecute = true });
        }

        private static void PlayFile(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || vm.Task.Title == "Initial System Setup") return;
            var path = vm.Task.OutputPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); return; }
            OpenFolder(vm);
        }

        private void ClearCompleted()
        {
            var completed = _downloads.Where(d => d.Task.Status == DownloadStatus.Completed).ToList();
            foreach (var vm in completed) { CleanupCookieFile(vm.Task); _downloads.Remove(vm); }
            HasCompletedDownloads = _downloads.Any(d => d.Task.Status == DownloadStatus.Completed);
            UpdateActiveCount();
            AppLogger.Log("[QUEUE] Completed tasks successfully purged from collection.");
        }

        private static void CleanupCookieFile(DownloadTask task)
        {
            if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath)) try { File.Delete(task.CookieFilePath); } catch { }
        }

        private void UpdateActiveCount()
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ActiveDownloadCount = _downloads.Count(d => d.Task.Status == DownloadStatus.Queued || d.Task.Status == DownloadStatus.Downloading);
                _trayService.UpdateTooltip(ActiveDownloadCount == 0 ? "YT Downloader Pro - Idle" : $"YT Downloader Pro - {ActiveDownloadCount} active");
            });
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
            var match = ResHeightRegex.Match(command);
            if (match.Success) return match.Groups[1].Value + "p";
            match = ResRegex.Match(command);
            if (match.Success) return match.Groups[1].Value + "p";
            return "";
        }

        private static string ExtractTitleHint(string command)
        {
            var match = OutputTemplateRegex.Match(command);
            if (match.Success) return Path.GetFileNameWithoutExtension(match.Groups[1].Value).Replace("%(title)s", "Fetching Title...").Replace("%(uploader)s", "Channel");
            return "Fetching Title...";
        }

        private static string ExtractSaveDirectory(string command)
        {
            var match = OutputTemplateRegex.Match(command);
            if (match.Success)
            {
                var template = match.Groups[1].Value;
                var dir = Path.GetDirectoryName(template);
                if (!string.IsNullOrEmpty(dir))
                {
                    dir = dir.Replace("/", "\\");
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (dir.StartsWith("~\\") || dir.StartsWith("~")) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), dir.Substring(1).TrimStart('\\'));
                    if (!Directory.Exists(dir)) try { Directory.CreateDirectory(dir); } catch { }
                    if (Directory.Exists(dir)) return dir;
                }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void ExitApplication()
        {
            AppLogger.Log("[SYSTEM] Application termination sequence invoked.");
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
