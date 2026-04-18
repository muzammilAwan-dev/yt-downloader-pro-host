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

        // MEMORY OPTIMIZATION: Separated the Full Disk Log from the UI Log
        private readonly StringBuilder _fullLogBuilder = new();
        private readonly Queue<string> _uiLogQueue = new();
        private const int MaxUiLogLines = 100; // Cap UI log at 100 lines to prevent WPF freezing
        private readonly object _logLock = new();
        
        // This is saved to the disk
        public string FullLogText => _fullLogBuilder.ToString();

        // This is bound to the WPF Textbox
        [ObservableProperty]
        private string _uiLogText = "";

        [ObservableProperty]
        private bool _logFileSaved;

        public void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                // 1. Add to the full log for the disk file
                _fullLogBuilder.AppendLine(line);
                
                // 2. Add to the rolling UI buffer (keeps memory usage tiny!)
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
            }
        }
    }
}
