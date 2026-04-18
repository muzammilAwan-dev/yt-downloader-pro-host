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
        private Process? _process;
        private readonly CancellationTokenSource _cts = new();
        private readonly StringBuilder _errorBuffer = new();
        private bool _downloadStarted;
        private bool _extractionComplete;
        private bool _disposed;

        private DateTime _lastUiUpdate = DateTime.MinValue;

        public event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        public event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        public event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;

        public bool IsRunning => _process != null && !_process.HasExited;

        public async Task ExecuteAsync(DownloadTask task)
        {
            _errorBuffer.Clear();
            _downloadStarted = false;
            _extractionComplete = false;
            task.ClearLog();

            // FIXED: Bulletproof Regex to strip redundant "yt-dlp" text from the payload
            var command = task.Command.Trim();
            command = Regex.Replace(command, @"^(?:yt-dlp\.exe|yt-dlp)\s+", "", RegexOptions.IgnoreCase);

            task.AppendLog("=== Download Task Started ===");
            task.AppendLog($"Command executed: yt-dlp.exe {command}");
            task.AppendLog("");

            try
            {
                var saveDirectory = ExtractSaveDirectory(command);

                if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath))
                {
                    command += $" --cookies \"{task.CookieFilePath}\"";
                    task.AppendLog($"[DEBUG] Injection: Cookies appended from {task.CookieFilePath}");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "yt-dlp.exe",
                    Arguments = command,
                    CreateNoWindow = true,
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
                    task.CompletedAt = DateTime.Now;
                    task.Speed = "";
                    task.Eta = "Done";
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
                    task.ErrorMessage = string.IsNullOrWhiteSpace(lastErrors)
                        ? $"Download failed (Code: {_process.ExitCode}). Check full logs for details."
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

            if (!_extractionComplete && (data.Contains("[youtube]") || data.Contains("[info]")))
            {
                _extractionComplete = true;
                if (task.Status != DownloadStatus.Downloading)
                {
                    task.Status = DownloadStatus.Downloading;
                    needsUiUpdate = true;
                }
            }

            if (data.Contains("[download]"))
            {
                _downloadStarted = true;
                if (task.Status != DownloadStatus.Downloading)
                {
                    task.Status = DownloadStatus.Downloading;
                    needsUiUpdate = true;
                }

                var percentMatch = Regex.Match(data, @"\[download\]\s+(?:(\d+\.?\d*)%|100%)");
                if (percentMatch.Success)
                {
                    if (double.TryParse(percentMatch.Groups[1].Value, out var percent))
                        task.Progress = Math.Min(percent, 100.0);
                    else if (data.Contains("100%"))
                        task.Progress = 100.0;
                    
                    if (task.Progress > oldProgress) needsUiUpdate = true;
                }

                var speedMatch = Regex.Match(data, @"at\s+([\d\.]+[KMG]iB/s)");
                if (speedMatch.Success) { task.Speed = speedMatch.Groups[1].Value; needsUiUpdate = true; }

                var etaMatch = Regex.Match(data, @"ETA\s+([\d:]+)");
                if (etaMatch.Success) { task.Eta = etaMatch.Groups[1].Value; needsUiUpdate = true; }
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
            {
                var destMatch = Regex.Match(data, @"Destination:\s+(.+)");
                if (destMatch.Success)
                {
                    task.OutputPath = destMatch.Groups[1].Value.Trim();
                    string tempFileName = Path.GetFileName(task.OutputPath);
                    if (!string.IsNullOrEmpty(tempFileName) && task.Title == "Unknown")
                        task.Title = tempFileName;
                    
                    OnInfoExtracted?.Invoke(this, new ExtractedInfoEventArgs(task.Id, task.Title, task.OutputPath));
                }
            }

            if ((data.Contains("[Merger]") || data.Contains("[ExtractAudio]") || data.Contains("[MoveFiles]")) 
                && data.Contains("Destination:"))
            {
                var destMatch = Regex.Match(data, @"Destination:\s+(.+)");
                if (destMatch.Success)
                {
                    task.OutputPath = destMatch.Groups[1].Value.Trim();
                    string tempFileName = Path.GetFileName(task.OutputPath);
                    if (!string.IsNullOrEmpty(tempFileName) && task.Title == "Unknown")
                        task.Title = tempFileName;
                }
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
            var match = Regex.Match(command, @"-o\s+""([^""]+)""");
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

            match = Regex.Match(command, @"-P\s+""([^""]+)""");
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

            // FIXED: Create an isolated log folder to keep user directories clean
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
