using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YTDLPHost.Models
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Paused,
        Completed,
        Error,
        Cancelled
    }

    public partial class DownloadTask : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private string _title = "Unknown";

        [ObservableProperty]
        private string _resolution = "";

        [ObservableProperty]
        private string _urlPayload = "";

        [ObservableProperty]
        private string _command = "";

        [ObservableProperty]
        private string _cookiePayload = "";

        [ObservableProperty]
        private string _cookieFilePath = "";

        [ObservableProperty]
        private double _progress = 0.0;

        [ObservableProperty]
        private string _speed = "";

        [ObservableProperty]
        private string _eta = "";

        // REQUIRED FOR UI: Tracks the total file size
        [ObservableProperty]
        private string _fileSize = "";

        [ObservableProperty]
        private DownloadStatus _status = DownloadStatus.Queued;

        [ObservableProperty]
        private string _errorMessage = "";

        [ObservableProperty]
        private string _outputPath = "";

        [ObservableProperty]
        private string _fileName = "";

        [ObservableProperty]
        private string _currentPhase = "Starting...";

        [ObservableProperty]
        private bool _isIndeterminate = false;

        [ObservableProperty]
        private string _playlistInfo = "";

        [ObservableProperty]
        private DateTime _queuedAt = DateTime.Now;

        [ObservableProperty]
        private DateTime? _startedAt;

        [ObservableProperty]
        private DateTime? _completedAt;

        // REQUIRED FOR CLEANUP: Tracks every file yt-dlp touches
        public HashSet<string> TrackedFiles { get; } = new();

        private readonly StringBuilder _fullLogBuilder = new();
        private readonly Queue<string> _uiLogQueue = new();
        private const int MaxUiLogLines = 100; 
        private readonly object _logLock = new();
        
        public string FullLogText => _fullLogBuilder.ToString();

        [ObservableProperty]
        private string _uiLogText = "";

        [ObservableProperty]
        private bool _logFileSaved;

        public void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                _fullLogBuilder.AppendLine(line);
                
                _uiLogQueue.Enqueue(line);
                if (_uiLogQueue.Count > MaxUiLogLines)
                {
                    _uiLogQueue.Dequeue();
                }

                UiLogText = string.Join(Environment.NewLine, _uiLogQueue);
            }
        }

        public void ClearLog()
        {
            lock (_logLock)
            {
                _fullLogBuilder.Clear();
                _uiLogQueue.Clear();
                UiLogText = "";
                LogFileSaved = false;
                TrackedFiles.Clear();
            }
        }
    }
}
