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

                    if (string.IsNullOrEmpty(task.OutputPath))
                    {
                        var destLine = _recentOutput.ToString().Split('\n')
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

            _recentOutput.AppendLine(data);
            if (_recentOutput.Length > 10000)
            {
                var text = _recentOutput.ToString();
                _recentOutput.Clear();
                _recentOutput.Append(text.Substring(text.Length - 5000));
            }

            if (task.Status != DownloadStatus.Downloading)
            {
                task.Status = DownloadStatus.Downloading;
            }

            if (data.Contains("[download]"))
            {
                var percentMatch = Regex.Match(data, @"\[download\]\s+(?:(\d+\.?\d*)%|100%)");
                if (percentMatch.Success)
                {
                    if (double.TryParse(percentMatch.Groups[1].Value, out var percent))
                    {
                        task.Progress = Math.Min(percent, 100.0);
                    }
                    else
                    {
                        task.Progress = 100.0;
                    }
                    OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, task.Progress, task.Speed, task.Eta));
                }

                var speedMatch = Regex.Match(data, @"at\s+([\d\.]+[KMG]iB/s)");
                if (speedMatch.Success)
                {
                    task.Speed = speedMatch.Groups[1].Value;
                }

                var etaMatch = Regex.Match(data, @"ETA\s+([\d:]+)");
                if (etaMatch.Success)
                {
                    task.Eta = etaMatch.Groups[1].Value;
                }

                if (data.Contains("100%") || data.Contains("100.0%"))
                {
                    task.Progress = 100.0;
                }

                OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, task.Progress, task.Speed, task.Eta));
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
            {
                var destMatch = Regex.Match(data, @"Destination:\s+(.+)");
                if (destMatch.Success)
                {
                    task.OutputPath = destMatch.Groups[1].Value.Trim();
                    task.FileName = Path.GetFileName(task.OutputPath);
                    if (!string.IsNullOrEmpty(task.FileName) && task.Title == "Unknown")
                        task.Title = task.FileName;
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
                    task.FileName = Path.GetFileName(task.OutputPath);
                    if (!string.IsNullOrEmpty(task.FileName) && task.Title == "Unknown")
                        task.Title = task.FileName;
                }
            }
        }

        private void HandleError(string? data)
        {
            if (!string.IsNullOrWhiteSpace(data))
            {
                _errorBuffer.AppendLine(data);
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
