using System;
using System.ComponentModel;
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
        private string _urlPayload = string.Empty;

        [ObservableProperty]
        private string _command = string.Empty;

        [ObservableProperty]
        private string? _cookiePayload;

        [ObservableProperty]
        private string _cookieFilePath = string.Empty;

        [ObservableProperty]
        private string _outputPath = string.Empty;

        [ObservableProperty]
        private string _title = "Unknown";

        [ObservableProperty]
        private DownloadStatus _status = DownloadStatus.Queued;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProgressPercent))]
        private double _progress = 0.0;

        [ObservableProperty]
        private string _speed = string.Empty;

        [ObservableProperty]
        private string _eta = string.Empty;

        [ObservableProperty]
        private string _resolution = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private DateTime _addedAt = DateTime.Now;

        [ObservableProperty]
        private DateTime? _completedAt;

        [ObservableProperty]
        private string _fileName = string.Empty;

        public int ProgressPercent => (int)_progress;

        public string StatusText => Status switch
        {
            DownloadStatus.Queued => "Queued",
            DownloadStatus.Downloading => "Downloading",
            DownloadStatus.Paused => "Paused",
            DownloadStatus.Completed => "Complete",
            DownloadStatus.Error => "Error",
            DownloadStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };

        public bool IsActive => Status == DownloadStatus.Queued || Status == DownloadStatus.Downloading;
        public bool IsCompleted => Status == DownloadStatus.Completed;
        public bool HasError => Status == DownloadStatus.Error;
        public bool IsCancellable => Status == DownloadStatus.Queued || Status == DownloadStatus.Downloading;
        public bool CanOpenFolder => Status == DownloadStatus.Completed && !string.IsNullOrEmpty(OutputPath);
        public bool CanPlay => Status == DownloadStatus.Completed && !string.IsNullOrEmpty(OutputPath);
    }
}
