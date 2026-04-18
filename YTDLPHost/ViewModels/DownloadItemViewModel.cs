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

        // Displays "YouTube Video..." until the real title is fetched by the runner
        public string DisplayTitle => (Task.Title == "Unknown" || Task.Title.Contains("Fetching")) 
            ? "YouTube Video..." 
            : Task.Title;

        public string ResolutionDisplay => Task.Resolution;
        public double Progress => Task.Progress;
        public string ErrorMessage => Task.ErrorMessage;

        /// <summary>
        /// THE ENGINE OF THE UI CARD:
        /// This property dynamically builds the status string based on the current download phase.
        /// Example: "Downloading Video | 45.5% of 63.31MiB | 1.2MiB/s | ETA: 00:05"
        /// </summary>
        public string ProgressDisplay
        {
            get
            {
                // Handle static states first
                if (Task.Status == DownloadStatus.Completed) return "Completed";
                if (Task.Status == DownloadStatus.Error) return "Error - Click for details";
                if (Task.Status == DownloadStatus.Cancelled) return "Cancelled";
                if (Task.Status == DownloadStatus.Paused) return "Paused";
                if (Task.Status == DownloadStatus.Queued) return "Waiting in Queue...";

                // Build the active download string
                string prefix = string.IsNullOrEmpty(Task.PlaylistInfo) 
                    ? Task.CurrentPhase 
                    : $"{Task.PlaylistInfo} - {Task.CurrentPhase}";

                // If we are in an indeterminate phase (like Merging), just show the phase name
                if (Task.IsIndeterminate) return prefix;
                
                // Construct the measurement string
                string sizeStr = string.IsNullOrEmpty(Task.FileSize) ? "" : $" of {Task.FileSize}";
                string details = $" | {Task.Progress:0.0}%{sizeStr}";
                
                if (!string.IsNullOrEmpty(Task.Speed)) 
                    details += $" | {Task.Speed}";
                
                if (!string.IsNullOrEmpty(Task.Eta) && Task.Eta != "Unknown") 
                    details += $" | ETA: {Task.Eta}";
                
                return $"{prefix}{details}";
            }
        }

        // Computed boolean properties for UI Binding (Visibility/Enablement)
        public bool IsCompleted => Task.Status == DownloadStatus.Completed;
        public bool IsActive => Task.Status == DownloadStatus.Downloading || Task.Status == DownloadStatus.Queued;
        public bool HasError => Task.Status == DownloadStatus.Error;
        public bool IsResumable => Task.Status == DownloadStatus.Paused || Task.Status == DownloadStatus.Error;
        public bool CanBePaused => Task.Status == DownloadStatus.Downloading;
        public bool CanBeCancelled => IsActive || Task.Status == DownloadStatus.Paused;
        public bool CanBeRemoved => Task.Status == DownloadStatus.Completed || Task.Status == DownloadStatus.Error || Task.Status == DownloadStatus.Cancelled;

        public ICommand ToggleLogCommand { get; }

        public DownloadItemViewModel(DownloadTask task)
        {
            Task = task;
            
            // Listen for changes in the Model (DownloadTask) and notify the UI View
            Task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Task.Status))
                {
                    RefreshStateProperties();
                }
                else if (e.PropertyName == nameof(Task.Speed) || 
                         e.PropertyName == nameof(Task.Eta) || 
                         e.PropertyName == nameof(Task.FileSize) || 
                         e.PropertyName == nameof(Task.CurrentPhase) || 
                         e.PropertyName == nameof(Task.PlaylistInfo) || 
                         e.PropertyName == nameof(Task.IsIndeterminate) ||
                         e.PropertyName == nameof(Task.Progress))
                {
                    // Any of these change, we must rebuild the entire ProgressDisplay string
                    OnPropertyChanged(nameof(ProgressDisplay));
                    OnPropertyChanged(nameof(Progress));
                }
                else if (e.PropertyName == nameof(Task.ErrorMessage))
                {
                    OnPropertyChanged(nameof(ErrorMessage));
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
            RefreshStateProperties();
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(Progress));
        }

        private void RefreshStateProperties()
        {
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsResumable));
            OnPropertyChanged(nameof(CanBePaused));
            OnPropertyChanged(nameof(CanBeCancelled));
            OnPropertyChanged(nameof(CanBeRemoved));
            OnPropertyChanged(nameof(ProgressDisplay));
        }
    }
}
