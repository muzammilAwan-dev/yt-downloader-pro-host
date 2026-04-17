using System;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Models;

namespace YTDLPHost.ViewModels
{
    public partial class DownloadItemViewModel : ObservableObject
    {
        public DownloadTask Task { get; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isExpanded;

        public DownloadItemViewModel(DownloadTask task)
        {
            Task = task;
            Task.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(string.Empty);
            };
        }

        public Guid Id => Task.Id;
        public string Title => Task.Title;
        public string FileName => Task.FileName;
        public DownloadStatus Status => Task.Status;
        public double Progress => Task.Progress;
        public int ProgressPercent => Task.ProgressPercent;
        public string Speed => Task.Speed;
        public string Eta => Task.Eta;
        public string Resolution => Task.Resolution;
        public string ErrorMessage => Task.ErrorMessage;
        public string StatusText => Task.StatusText;
        public DateTime AddedAt => Task.AddedAt;
        public bool IsActive => Task.IsActive;
        public bool IsCompleted => Task.IsCompleted;
        public bool HasError => Task.HasError;
        public bool IsCancellable => Task.IsCancellable;
        public bool CanOpenFolder => Task.CanOpenFolder;
        public bool CanPlay => Task.CanPlay;
        public string OutputPath => Task.OutputPath;

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Task.Title) && Task.Title != "Unknown")
                    return Task.Title;
                if (!string.IsNullOrEmpty(Task.FileName))
                    return Task.FileName;
                return "YouTube Video";
            }
        }

        public string ResolutionDisplay
        {
            get
            {
                if (!string.IsNullOrEmpty(Task.Resolution))
                    return Task.Resolution;
                return "";
            }
        }

        public string ProgressDisplay
        {
            get
            {
                if (Task.Status == DownloadStatus.Downloading)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    if (Task.Progress > 0)
                        parts.Add($"{Task.Progress:F1}%");
                    if (!string.IsNullOrEmpty(Task.Speed))
                        parts.Add(Task.Speed);
                    if (!string.IsNullOrEmpty(Task.Eta) && Task.Eta != "Done")
                        parts.Add($"ETA {Task.Eta}");
                    return string.Join("  |  ", parts);
                }
                if (Task.Status == DownloadStatus.Completed)
                    return "Complete";
                if (Task.Status == DownloadStatus.Error)
                    return $"Error: {Task.ErrorMessage}";
                if (Task.Status == DownloadStatus.Queued)
                    return "Waiting...";
                if (Task.Status == DownloadStatus.Cancelled)
                    return "Cancelled";
                return Task.StatusText;
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(string.Empty);
        }
    }
}
