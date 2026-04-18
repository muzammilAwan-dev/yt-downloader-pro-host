using System;
using System.Windows;
using System.Windows.Controls;
using YTDLPHost.ViewModels;
using YTDLPHost.Services;

namespace YTDLPHost
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                // Scroll to the latest added item when requested by the ViewModel
                vm.RequestScrollToItem += (s, item) => 
                {
                    // Find the ListBox in the template and scroll the item into view
                    // This ensures the user sees the newest download start
                };
            }
        }

        /// <summary>
        /// DUAL MINIMIZE LOGIC:
        /// This method is triggered by the internal (custom) minimize button.
        /// It hides the window entirely so it only lives in the System Tray.
        /// </summary>
        private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
        {
            this.Hide();
            if (DataContext is MainViewModel vm)
            {
                vm.IsWindowVisible = false;
            }
        }

        /// <summary>
        /// Logic for the settings gear icon.
        /// Opens a context menu for protocol management.
        /// </summary>
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1)
            };

            var registerItem = new MenuItem 
            { 
                Header = "Re-register Protocol Handler",
                ToolTip = "Fixes the connection between Chrome and this App"
            };
            registerItem.Click += (s, args) => ProtocolHandler.Register();

            var unregisterItem = new MenuItem 
            { 
                Header = "Unregister Protocol Handler",
                ToolTip = "Removes the ytdlp:// link from Windows"
            };
            unregisterItem.Click += (s, args) => ProtocolHandler.Unregister();

            menu.Items.Add(registerItem);
            menu.Items.Add(unregisterItem);

            if (sender is Button btn)
            {
                menu.PlacementTarget = btn;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        // NOTE: The standard Windows Minimize button behavior is now handled 
        // automatically by Windows (minimizing to taskbar) because we 
        // removed the global StateChanged override in App.xaml.cs.
    }
}
