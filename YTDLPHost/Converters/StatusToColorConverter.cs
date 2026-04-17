using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using YTDLPHost.Models;

namespace YTDLPHost.Converters
{
    [ValueConversion(typeof(DownloadStatus), typeof(SolidColorBrush))]
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                return status switch
                {
                    DownloadStatus.Queued => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x71, 0x71, 0x71)),
                    DownloadStatus.Downloading => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0xA6, 0xFF)),
                    DownloadStatus.Paused => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00)),
                    DownloadStatus.Completed => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
                    DownloadStatus.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36)),
                    DownloadStatus.Cancelled => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x71, 0x71, 0x71))
                };
            }
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x71, 0x71, 0x71));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(DownloadStatus), typeof(string))]
    public class StatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                return status switch
                {
                    DownloadStatus.Queued => "Queued",
                    DownloadStatus.Downloading => "Downloading",
                    DownloadStatus.Paused => "Paused",
                    DownloadStatus.Completed => "Complete",
                    DownloadStatus.Error => "Error",
                    DownloadStatus.Cancelled => "Cancelled",
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(DownloadStatus), typeof(bool))]
    public class StatusToSpinConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is DownloadStatus.Downloading;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}