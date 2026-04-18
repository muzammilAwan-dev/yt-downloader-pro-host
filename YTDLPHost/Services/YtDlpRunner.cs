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
        private readonly StringBuilder _recentOutput = new();
        
        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly object _bufferLock = new();

        public event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        public event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        public event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;

        public bool IsRunning => _process != null && !_process.HasExited;

        public async Task ExecuteAsync(DownloadTask task)
        {
            _errorBuffer.Clear();
            _recentOutput.Clear();

            try
            {
                var command = task.Command;
                var saveDirectory = ExtractSaveDirectory(command);

                if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath))
                {
                    command += $" --cookies \"{task.CookieFilePath}\"";
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
                _process.ErrorDataReceived += (s, e) => HandleError(e.Data);

                var tcs = new TaskCompletionSource<bool>();
                _process.Exited += (s, e) => tcs.TrySetResult(true);

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                await tcs.Task;

                if (_process.ExitCode == 0)
                {
                    task.Status = DownloadStatus.Completed;
                    task.Progress = 100.0;
                    task.CompletedAt = DateTime.Now;
                    task.Speed = string.Empty;
                    task.Eta = "Done";

                    string finalOutputString;
                    lock (_bufferLock)
                    {
                        finalOutputString = _recentOutput.ToString();
                    }

                    if (string.IsNullOrEmpty(task.OutputPath))
                    {
                        var destLine = finalOutputString.Split('\n')
                            .LastOrDefault(l => l.Contains("Destination:", StringComparison.OrdinalIgnoreCase));
                        if (destLine != null)
                        {
                            var match = Regex.Match(destLine, @"Destination:\s+(.+)");
                            if (match.Success)
                            {
                                task.OutputPath = match.Groups[1].Value.Trim();
                                task.FileName = Path.GetFileName(task.OutputPath);
                                if (!string.IsNullOrEmpty(task.FileName) && task.Title == "Unknown")
                                    task.Title = task.FileName;
                            }
                        }
                    }

                    OnDownloadComplete?.Invoke(this, new CompleteEventArgs(task.Id, task.OutputPath, task.Title));
                }
                else
                {
                    var lastErrors = string.Join("\n", _errorBuffer.ToString()
                        .Split('\n')
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .TakeLast(3));

                    task.Status = DownloadStatus.Error;
                    task.ErrorMessage = string.IsNullOrWhiteSpace(lastErrors)
                        ? "Download failed. Check yt-dlp output for details."
                        : lastErrors;

                    OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Error;
                task.ErrorMessage = $"Execution error: {ex.Message}";
                OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
            }
            finally
            {
                CleanupProcess();
            }
        }

        private void HandleOutput(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            lock (_bufferLock)
            {
                _recentOutput.AppendLine(data);
                if (_recentOutput.Length > 10000)
                {
                    var text = _recentOutput.ToString();
                    _recentOutput.Clear();
                    _recentOutput.Append(text.Substring(text.Length - 5000));
                }
            }

            // Perform Regex calculations safely on the background thread
            bool needsUiUpdate = false;
            DownloadStatus newStatus = task.Status;
            double newProgress = task.Progress;
            string newSpeed = task.Speed;
            string newEta = task.Eta;
            string newOutputPath = task.OutputPath;
            string newTitle = task.Title;

            if (newStatus != DownloadStatus.Downloading)
            {
                newStatus = DownloadStatus.Downloading;
                needsUiUpdate = true;
            }

            if (data.Contains("[download]"))
            {
                var percentMatch = Regex.Match(data, @"\[download\]\s+(?:(\d+\.?\d*)%|100%)");
                if (percentMatch.Success)
                {
                    newProgress = double.TryParse(percentMatch.Groups[1].Value, out var percent) ? Math.Min(percent, 100.0) : 100.0;
                    needsUiUpdate = true;
                }

                var speedMatch = Regex.Match(data, @"at\s+([\d\.]+[KMG]iB/s)");
                if (speedMatch.Success) { newSpeed = speedMatch.Groups[1].Value; needsUiUpdate = true; }

                var etaMatch = Regex.Match(data, @"ETA\s+([\d:]+)");
                if (etaMatch.Success) { newEta = etaMatch.Groups[1].Value; needsUiUpdate = true; }
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
            {
                var destMatch = Regex.Match(data, @"Destination:\s+(.+)");
                if (destMatch.Success)
                {
                    newOutputPath = destMatch.Groups[1].Value.Trim();
                    string tempFileName = Path.GetFileName(newOutputPath);
                    if (!string.IsNullOrEmpty(tempFileName) && newTitle == "Unknown") newTitle = tempFileName;
                    OnInfoExtracted?.Invoke(this, new ExtractedInfoEventArgs(task.Id, newTitle, newOutputPath));
                    needsUiUpdate = true;
                }
            }

            // Push ALL calculated UI updates safely to the main Dispatcher thread at once
            if (needsUiUpdate)
            {
                var now = DateTime.Now;
                if ((now - _lastUpdate).TotalMilliseconds >= 100 || newProgress >= 100.0)
                {
                    _lastUpdate = now;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        task.Status = newStatus;
                        task.Progress = newProgress;
                        task.Speed = newSpeed;
                        task.Eta = newEta;
                        task.OutputPath = newOutputPath;
                        task.Title = newTitle;
                    });
                    
                    OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, newProgress, newSpeed, newEta));
                }
            }
        }

        private void HandleError(string? data)
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                lock (_bufferLock)
                {
                    _errorBuffer.AppendLine(data);
                }
            }
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
                    if (Directory.Exists(dir))
                        return dir;
                }
            }

            match = Regex.Match(command, @"-P\s+""([^""]+)""");
            if (match.Success)
            {
                var dir = Environment.ExpandEnvironmentVariables(match.Groups[1].Value);
                if (Directory.Exists(dir))
                    return dir;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public void Cancel()
        {
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
