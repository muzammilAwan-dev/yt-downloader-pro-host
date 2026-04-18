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
        [ObservableProperty]
        private DownloadTask _task;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isLogVisible;

        public Guid Id => Task.Id;
        public string DisplayTitle => Task.Title == "Unknown" ? "YouTube Video..." : Task.Title;
        public string ProgressDisplay => $"{Task.Status} | {Task.Speed} | ETA: {Task.Eta}";
        public string ResolutionDisplay => Task.Resolution;
        public bool IsCompleted => Task.Status == DownloadStatus.Completed;
        public bool IsActive => Task.Status == DownloadStatus.Downloading || Task.Status == DownloadStatus.Queued;
        public bool HasError => Task.Status == DownloadStatus.Error;
        public bool IsCancellable => Task.Status == DownloadStatus.Downloading || Task.Status == DownloadStatus.Queued;
        
        // NEW: Property to determine if we can resume the download
        public bool IsResumable => Task.Status == DownloadStatus.Cancelled || Task.Status == DownloadStatus.Error;
        
        public string ErrorMessage => Task.ErrorMessage;
        public double Progress => Task.Progress;

        public ICommand ToggleLogCommand { get; }

        public DownloadItemViewModel(DownloadTask task)
        {
            Task = task;
            
            Task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Task.Status))
                {
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(IsCancellable));
                    OnPropertyChanged(nameof(IsResumable));
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
                else if (e.PropertyName == nameof(Task.Speed) || e.PropertyName == nameof(Task.Eta))
                {
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
                else if (e.PropertyName == nameof(Task.ErrorMessage))
                {
                    OnPropertyChanged(nameof(ErrorMessage));
                }
                else if (e.PropertyName == nameof(Task.Progress))
                {
                    OnPropertyChanged(nameof(Progress));
                }
                else if (e.PropertyName == nameof(Task.Title))
                {
                    OnPropertyChanged(nameof(DisplayTitle));
                }
                else if (e.PropertyName == nameof(Task.Resolution))
                {
                    OnPropertyChanged(nameof(ResolutionDisplay));
                }
            };

            ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);
        }

        public void Refresh()
        {
            OnPropertyChanged(string.Empty);
        }
    }
}
