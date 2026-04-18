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

        private Process? _process;
        private readonly CancellationTokenSource _cts = new();
        private readonly StringBuilder _errorBuffer = new();
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
            _extractionComplete = false;
            task.ClearLog();
            task.CurrentPhase = "Starting...";
            task.IsIndeterminate = true;

            var command = task.Command.Trim();
            command = CmdTrimRegex.Replace(command, "");

            task.AppendLog("=== Download Task Started ===");
            task.AppendLog($"Command executed: yt-dlp.exe {command}");

            try
            {
                var saveDirectory = ExtractSaveDirectory(command);
                if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath))
                    command += $" --cookies \"{task.CookieFilePath}\"";

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
                    task.IsIndeterminate = false;
                    task.CurrentPhase = "Complete";
                    task.CompletedAt = DateTime.Now;
                    OnDownloadComplete?.Invoke(this, new CompleteEventArgs(task.Id, task.OutputPath, task.Title));
                }
                else
                {
                    task.Status = DownloadStatus.Error;
                    task.ErrorMessage = $"Failed (Code: {_process.ExitCode})";
                    OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Error;
                task.ErrorMessage = ex.Message;
                OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, ex.Message));
            }
            finally { CleanupProcess(); SaveLogsToDisk(task); }
        }

        private void HandleOutput(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            task.AppendLog(data);
            bool needsUiUpdate = false;

            var fileTrackMatch = FileTrackerRegex.Match(data);
            if (fileTrackMatch.Success)
            {
                string path = fileTrackMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(path)) task.TrackedFiles.Add(path);
            }

            // Phase updates
            if (data.Contains("Writing video subtitles", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Downloading Subtitles...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("Downloading video thumbnail", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Downloading Thumbnail...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("[SponsorBlock]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Removing Sponsors...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }

            if (data.Contains("[download]") && data.Contains("%"))
            {
                task.IsIndeterminate = false;

                // FIX: Transition from Thumbnail/Subtitle text to Video download text
                if (task.CurrentPhase == "Downloading Thumbnail..." || task.CurrentPhase == "Downloading Subtitles...")
                {
                    task.CurrentPhase = "Downloading Video...";
                    task.FileSize = ""; // Reset size to capture real media size
                    needsUiUpdate = true;
                }

                var sizeMatch = SizeRegex.Match(data);
                if (sizeMatch.Success && string.IsNullOrEmpty(task.FileSize)) 
                { 
                    task.FileSize = sizeMatch.Groups[1].Value; 
                    needsUiUpdate = true; 
                }

                var percentMatch = PercentRegex.Match(data);
                if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out var percent))
                {
                    percent = Math.Min(percent, 100.0);
                    if (percent < task.Progress && task.Progress >= 90.0 && task.CurrentPhase == "Downloading Video...")
                    {
                        task.CurrentPhase = "Downloading Audio...";
                        task.FileSize = ""; 
                    }
                    task.Progress = percent;
                    needsUiUpdate = true;
                }

                var speedMatch = SpeedRegex.Match(data);
                if (speedMatch.Success) { task.Speed = speedMatch.Groups[1].Value; needsUiUpdate = true; }

                var etaMatch = EtaRegex.Match(data);
                if (etaMatch.Success) { task.Eta = etaMatch.Groups[1].Value; needsUiUpdate = true; }
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase) && !data.Contains("[Merger]"))
            {
                var destMatch = FileTrackerRegex.Match(data);
                if (destMatch.Success)
                {
                    task.OutputPath = destMatch.Groups[1].Value.Trim();
                    string clean = Path.GetFileNameWithoutExtension(task.OutputPath);
                    clean = Regex.Replace(clean, @"\.(f\w+|en-orig|en|vtt|webp|jpg)$", "", RegexOptions.IgnoreCase);

                    if (!string.IsNullOrEmpty(clean) && (task.Title.Contains("Fetching") || task.Title == "Unknown"))
                    {
                        task.Title = clean;
                        OnInfoExtracted?.Invoke(this, new ExtractedInfoEventArgs(task.Id, task.Title, task.OutputPath));
                    }
                }
            }

            if (data.Contains("[Merger]") || data.Contains("[MoveFiles]"))
            {
                task.CurrentPhase = "Merging & Finalizing...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
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

        private void CleanupProcess() { try { if (_process != null && !_process.HasExited) _process.Kill(true); _process?.Dispose(); } catch { } }
        private void SaveLogsToDisk(DownloadTask task) { /* Existing logging logic */ }
        private static string ExtractSaveDirectory(string cmd) { /* Existing logic */ return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        public void Cancel() { _disposed = true; try { _cts.Cancel(); CleanupProcess(); } catch { } }
        public void Dispose() { Cancel(); _cts.Dispose(); }
    }

    public class ProgressEventArgs : EventArgs { public Guid TaskId { get; } public double Percent { get; } public string Speed { get; } public string Eta { get; } public ProgressEventArgs(Guid t, double p, string s, string e) { TaskId = t; Percent = p; Speed = s; Eta = e; } }
    public class CompleteEventArgs : EventArgs { public Guid TaskId { get; } public string FilePath { get; } public string Title { get; } public CompleteEventArgs(Guid t, string f, string ti) { TaskId = t; FilePath = f; Title = ti; } }
    public class DownloadErrorEventArgs : EventArgs { public Guid TaskId { get; } public string ErrorMessage { get; } public DownloadErrorEventArgs(Guid t, string e) { TaskId = t; ErrorMessage = e; } }
    public class ExtractedInfoEventArgs : EventArgs { public Guid TaskId { get; } public string Title { get; } public string OutputPath { get; } public ExtractedInfoEventArgs(Guid t, string ti, string o) { TaskId = t; Title = ti; OutputPath = o; } }
}
