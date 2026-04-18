using System;
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

        // NEW: UI state trackers for audio/video/merge phases and playlists
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

        private readonly StringBuilder _logBuilder = new();
        private readonly object _logLock = new();
        
        [ObservableProperty]
        private string _fullLogText = "";

        [ObservableProperty]
        private bool _logFileSaved;

        public void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                _logBuilder.AppendLine(line);
                FullLogText = _logBuilder.ToString();
            }
        }

        public void ClearLog()
        {
            lock (_logLock)
            {
                _logBuilder.Clear();
                FullLogText = "";
                LogFileSaved = false;
            }
        }
    }
}
