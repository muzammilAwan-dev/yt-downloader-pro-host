using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YTDLPHost.ViewModels;
using YTDLPHost.Services;

namespace YTDLPHost
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RequestScrollToItem += OnRequestScrollToItem;
            }
        }

        private void OnRequestScrollToItem(object? sender, DownloadItemViewModel item)
        {
            // The ListBox will automatically show the item due to selection
            // Could implement scroll-into-view here if needed
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };

            var registerItem = new MenuItem
            {
                Header = "Re-register Protocol Handler",
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Background = System.Windows.Media.Brushes.Transparent
            };
            registerItem.Click += (s, args) =>
            {
                ProtocolHandler.Register();
                System.Windows.MessageBox.Show(this, "Protocol handler registered.", "YT Downloader Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var unregisterItem = new MenuItem
            {
                Header = "Unregister Protocol Handler",
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Background = System.Windows.Media.Brushes.Transparent
            };
            unregisterItem.Click += (s, args) =>
            {
                ProtocolHandler.Unregister();
                System.Windows.MessageBox.Show(this, "Protocol handler unregistered.", "YT Downloader Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var exitItem = new MenuItem
            {
                Header = "Exit",
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Background = System.Windows.Media.Brushes.Transparent
            };
            exitItem.Click += (s, args) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.ExitCommand.Execute(null);
                }
            };

            menu.Items.Add(registerItem);
            menu.Items.Add(unregisterItem);
            menu.Items.Add(new Separator { Background = (System.Windows.Media.Brush)FindResource("BorderBrush") });
            menu.Items.Add(exitItem);

            if (sender is System.Windows.Controls.Button btn)
            {
                menu.PlacementTarget = btn;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }
    }
}
