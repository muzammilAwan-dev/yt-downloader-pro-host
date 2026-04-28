using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    public class YtDlpRunner : IDisposable
    {
        private static readonly Regex CmdTrimRegex = new(@"^(?:yt-dlp\.exe|yt-dlp)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PercentRegex = new(@"\[download\]\s+(?:(\d+\.?\d*)%|100%)", RegexOptions.Compiled);
        private static readonly Regex SpeedRegex = new(@"at\s+([\d\.]+[KMG]iB/s)", RegexOptions.Compiled);
        private static readonly Regex EtaRegex = new(@"ETA\s+([\d:]+)", RegexOptions.Compiled);
        private static readonly Regex SizeRegex = new(@"of\s+([\d\.]+[KMG]iB)", RegexOptions.Compiled);
        private static readonly Regex PlaylistRegex = new(@"Downloading item (\d+) of (\d+)", RegexOptions.Compiled);
        private static readonly Regex OutputTemplateRegex = new(@"-o\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex PathTemplateRegex = new(@"-P\s+""([^""]+)""", RegexOptions.Compiled);
        
        private static readonly Regex FileTrackerRegex = new(@"(?:Destination:|Writing video \w+ to:|Writing video \w+ \d+ to:)\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AlreadyDownloadedRegex = new(@"\[download\]\s+(.*?)\s+has already been downloaded", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private Process? _process;
        private readonly CancellationTokenSource _cts = new();
        private readonly StringBuilder _errorBuffer = new();
        private bool _extractionComplete;
        private bool _disposed;
        
        private bool _hasStartedVideoMedia;

        private DateTime _lastUiUpdate = DateTime.MinValue;

        public event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        public event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        public event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;

        public bool IsRunning => _process != null && !_process.HasExited;

        public async Task ExecuteAsync(DownloadTask task)
        {
            _errorBuffer.Clear();
            _extractionComplete = false;
            _hasStartedVideoMedia = false; 
            
            task.ClearLog();
            task.CurrentPhase = "Starting...";
            task.IsIndeterminate = true;

            var command = task.Command.Trim();
            command = CmdTrimRegex.Replace(command, "");

            try
            {
                var saveDirectory = ExtractSaveDirectory(command);

                string engineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YT Downloader Pro", "Engine");
                string ytdlpPath = Path.Combine(engineDir, "yt-dlp.exe");

                // THE FIX: Explicitly inject the FFmpeg location so it can find it regardless of the WorkingDirectory
                if (!command.Contains("--ffmpeg-location"))
                {
                    command += $" --ffmpeg-location \"{engineDir}\"";
                }

                if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath))
                {
                    command += $" --cookies \"{task.CookieFilePath}\"";
                }

                task.AppendLog("=== Download Task Started ===");
                task.AppendLog($"Command executed: yt-dlp.exe {command}");
                task.AppendLog("");

                var psi = new ProcessStartInfo
                {
                    FileName = ytdlpPath, 
                    Arguments = command,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, 
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = saveDirectory,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.OutputDataReceived += (s, e) => HandleOutput(e.Data, task);
                _process.ErrorDataReceived += (s, e) => HandleError(e.Data, task);

                var tcs = new TaskCompletionSource<bool>();
                _process.Exited += (s, e) => tcs.TrySetResult(true);

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                await tcs.Task;

                if (_disposed) return;

                if (_process.ExitCode == 0)
                {
                    task.Status = DownloadStatus.Completed;
                    task.Progress = 100.0;
                    task.IsIndeterminate = false;
                    task.CurrentPhase = "Complete";
                    task.CompletedAt = DateTime.Now;
                    task.Speed = "";
                    task.Eta = "";
                    task.AppendLog("");
                    task.AppendLog("=== Download Finished Successfully ===");

                    OnDownloadComplete?.Invoke(this, new CompleteEventArgs(task.Id, task.OutputPath, task.Title));
                }
                else
                {
                    string lastErrors = string.Join("\n", _errorBuffer.ToString()
                        .Split('\n')
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .TakeLast(3));

                    task.Status = DownloadStatus.Error;
                    task.IsIndeterminate = false;
                    task.ErrorMessage = string.IsNullOrWhiteSpace(lastErrors)
                        ? $"Download failed (Code: {_process.ExitCode}). Check logs for details."
                        : lastErrors;
                    
                    task.AppendLog("");
                    task.AppendLog($"=== ERROR (Code: {_process.ExitCode}) ===");
                    task.AppendLog(task.ErrorMessage);

                    OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                task.Status = DownloadStatus.Error;
                task.IsIndeterminate = false;
                task.ErrorMessage = $"Execution error: {ex.Message}";
                task.AppendLog($"[DEBUG] Critical Exception: {ex.Message}");
                OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
            }
            finally
            {
                task.AppendLog($"=== Log End {DateTime.Now} ===");
                CleanupProcess();
                SaveLogsToDisk(task);
            }
        }

        private void HandleOutput(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            task.AppendLog(data);
            bool needsUiUpdate = false;
            double oldProgress = task.Progress;

            if (data.Contains("[download] Downloading item", StringComparison.OrdinalIgnoreCase))
            {
                var match = PlaylistRegex.Match(data);
                if (match.Success)
                {
                    task.PlaylistInfo = $"Item {match.Groups[1].Value}/{match.Groups[2].Value}";
                    
                    _extractionComplete = false; 
                    _hasStartedVideoMedia = false; 
                    task.Progress = 0.0;
                    task.CurrentPhase = "Starting...";
                    task.FileSize = "";
                    task.Speed = "";
                    task.Eta = "";
                    task.Title = "Fetching Title..."; 
                    needsUiUpdate = true;
                }
                return; 
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase) || 
                data.Contains("Writing video", StringComparison.OrdinalIgnoreCase) || 
                data.Contains("has already been downloaded", StringComparison.OrdinalIgnoreCase))
            {
                var fileTrackMatch = FileTrackerRegex.Match(data);
                var alreadyDownloadedMatch = AlreadyDownloadedRegex.Match(data);

                if ((fileTrackMatch.Success || alreadyDownloadedMatch.Success) && 
                    !data.Contains("[Merger]") && !data.Contains("[ExtractAudio]"))
                {
                    string path = fileTrackMatch.Success ? fileTrackMatch.Groups[1].Value.Trim() : alreadyDownloadedMatch.Groups[1].Value.Trim();
                    
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        task.TrackedFiles.Add(path);
                        task.OutputPath = path;
                        
                        string cleanTitle = Path.GetFileNameWithoutExtension(path);
                        string ext = Path.GetExtension(path).ToLower();
                        
                        cleanTitle = Regex.Replace(cleanTitle, @"\.(f\w+|en-orig|en|vtt|webp|jpg)$", "", RegexOptions.IgnoreCase);

                        if (!string.IsNullOrEmpty(cleanTitle) && 
                           (task.Title.Contains("Fetching Title") || task.Title.Contains("YouTube Video") || task.Title == "Unknown"))
                        {
                            task.Title = cleanTitle;
                            OnInfoExtracted?.Invoke(this, new ExtractedInfoEventArgs(task.Id, task.Title, task.OutputPath));
                        }

                        task.Progress = alreadyDownloadedMatch.Success ? 100.0 : 0.0;
                        task.FileSize = "";
                        oldProgress = task.Progress;

                        if (ext is ".vtt" or ".srt" or ".ass" or ".ttml" or ".lrc" or ".sbv" or ".ssa" or ".sub")
                        {
                            task.CurrentPhase = "Downloading Subtitles...";
                        }
                        else if (ext is ".webp" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff")
                        {
                            task.CurrentPhase = "Downloading Thumbnail...";
                        }
                        else if (ext is ".m4a" or ".mp3" or ".ogg" or ".wav" or ".aac" or ".flac" or ".opus" or ".wma" || task.Resolution == "Audio")
                        {
                            task.CurrentPhase = "Downloading Audio...";
                        }
                        else
                        {
                            if (!_hasStartedVideoMedia) 
                            {
                                task.CurrentPhase = "Downloading Video...";
                                _hasStartedVideoMedia = true; 
                            }
                            else 
                            {
                                task.CurrentPhase = "Downloading Audio...";
                            }
                        }
                        
                        needsUiUpdate = true;
                    }
                }
            }

            if (!_extractionComplete && (data.Contains("[youtube]", StringComparison.OrdinalIgnoreCase) || data.Contains("[info]", StringComparison.OrdinalIgnoreCase)))
            {
                if (task.CurrentPhase == "Starting...")
                {
                    _extractionComplete = true;
                    task.CurrentPhase = "Extracting Info...";
                    if (task.Status != DownloadStatus.Downloading)
                    {
                        task.Status = DownloadStatus.Downloading;
                    }
                    needsUiUpdate = true;
                }
            }

            if (data.Contains("[SponsorBlock]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Removing Sponsors...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Converting Audio...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("[Metadata]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Adding Metadata...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("[Merger]", StringComparison.OrdinalIgnoreCase) || data.Contains("[MoveFiles]", StringComparison.OrdinalIgnoreCase) || data.Contains("Fixing MPEG-TS", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Merging & Finalizing...";
                task.IsIndeterminate = true;
                task.Speed = "";
                task.Eta = "";
                needsUiUpdate = true;

                if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    var destMatch = FileTrackerRegex.Match(data);
                    if (destMatch.Success) task.OutputPath = destMatch.Groups[1].Value.Trim();
                }
            }

            if (data.Contains("[download]", StringComparison.OrdinalIgnoreCase) && data.Contains("%"))
            {
                task.IsIndeterminate = false; 
                
                if (task.Status != DownloadStatus.Downloading)
                {
                    task.Status = DownloadStatus.Downloading;
                    needsUiUpdate = true;
                }

                var sizeMatch = SizeRegex.Match(data);
                if (sizeMatch.Success && string.IsNullOrEmpty(task.FileSize)) 
                { 
                    task.FileSize = sizeMatch.Groups[1].Value; 
                    needsUiUpdate = true; 
                }

                var percentMatch = PercentRegex.Match(data);
                if (percentMatch.Success)
                {
                    if (double.TryParse(percentMatch.Groups[1].Value, out var percent))
                    {
                        task.Progress = Math.Min(percent, 100.0);
                    }
                    else if (data.Contains("100%")) 
                    {
                        task.Progress = 100.0;
                    }
                    
                    if (task.Progress != oldProgress) needsUiUpdate = true;
                }

                var speedMatch = SpeedRegex.Match(data);
                if (speedMatch.Success) { task.Speed = speedMatch.Groups[1].Value; needsUiUpdate = true; }

                var etaMatch = EtaRegex.Match(data);
                if (etaMatch.Success) { task.Eta = etaMatch.Groups[1].Value; needsUiUpdate = true; }
            }

            if (needsUiUpdate)
            {
                var now = DateTime.Now;
                if ((now - _lastUiUpdate).TotalMilliseconds >= 100 || task.Progress >= 100.0)
                {
                    _lastUiUpdate = now;
                    OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, task.Progress, task.Speed, task.Eta));
                }
            }
        }

        private void HandleError(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            _errorBuffer.AppendLine(data);
            task.AppendLog($"[stderr] {data}");
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
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (Directory.Exists(dir)) return dir;
                }
            }

            match = PathTemplateRegex.Match(command);
            if (match.Success)
            {
                var dir = Environment.ExpandEnvironmentVariables(match.Groups[1].Value);
                if (Directory.Exists(dir)) return dir;
            }

            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath)) return downloadsPath;
            
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void SaveLogsToDisk(DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(task.OutputPath)) return;

            string saveDir = Path.GetDirectoryName(task.OutputPath) ?? "";
            if (string.IsNullOrEmpty(saveDir)) return;

            string logDir = Path.Combine(saveDir, "YTDLP-Video-logs");
            if (!Directory.Exists(logDir))
            {
                try { Directory.CreateDirectory(logDir); } catch { return; }
            }

            try
            {
                string baseFileName = Path.GetFileNameWithoutExtension(task.FileName);
                if (string.IsNullOrWhiteSpace(baseFileName)) baseFileName = Path.GetFileNameWithoutExtension(task.OutputPath);
                if (string.IsNullOrWhiteSpace(baseFileName)) baseFileName = task.Title;

                string sanitizedTitle = Regex.Replace(baseFileName, @"[^\w\-\.]", "_");
                string logFileName = $"{sanitizedTitle}.log";
                string finalLogPath = Path.Combine(logDir, logFileName);

                File.WriteAllText(finalLogPath, task.FullLogText, Encoding.UTF8);
                task.AppendLog($"[INFO] Full logs saved to: {finalLogPath}");
                task.LogFileSaved = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save merged log to disk: {ex.Message}");
            }
        }

        public void Cancel()
        {
            _disposed = true;
            try
            {
                _cts.Cancel();
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch { }
        }

        private void CleanupProcess()
        {
            try
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                    _process.Dispose();
                    _process = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Cancel();
            CleanupProcess();
            _cts.Dispose();
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public double Percent { get; }
        public string Speed { get; }
        public string Eta { get; }

        public ProgressEventArgs(Guid taskId, double percent, string speed, string eta)
        {
            TaskId = taskId;
            Percent = percent;
            Speed = speed;
            Eta = eta;
        }
    }

    public class CompleteEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public string FilePath { get; }
        public string Title { get; }

        public CompleteEventArgs(Guid taskId, string filePath, string title)
        {
            TaskId = taskId;
            FilePath = filePath;
            Title = title;
        }
    }

    public class DownloadErrorEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public string ErrorMessage { get; }

        public DownloadErrorEventArgs(Guid taskId, string errorMessage)
        {
            TaskId = taskId;
            ErrorMessage = errorMessage;
        }
    }

    public class ExtractedInfoEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public string Title { get; }
        public string OutputPath { get; }

        public ExtractedInfoEventArgs(Guid taskId, string title, string outputPath)
        {
            TaskId = taskId;
            Title = title;
            OutputPath = outputPath;
        }
    }
}
