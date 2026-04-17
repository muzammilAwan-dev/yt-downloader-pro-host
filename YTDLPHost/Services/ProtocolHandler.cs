using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace YTDLPHost.Services
{
    public static class ProtocolHandler
    {
        private const string ProtocolKey = @"Software\Classes\ytdlp";
        private const string ProtocolName = "URL:YT Downloader Protocol";
        private const string PipeName = "YTDLPHost_Pipe";

        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command");
                if (key == null)
                    return false;

                var value = key.GetValue("") as string;
                if (string.IsNullOrEmpty(value))
                    return false;

                var exePath = GetExecutablePath();
                return value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void Register()
        {
            try
            {
                if (IsRegistered())
                    return;

                var exePath = GetExecutablePath();

                using var protocolKey = Registry.CurrentUser.CreateSubKey(ProtocolKey);
                protocolKey?.SetValue("", ProtocolName);
                protocolKey?.SetValue("URL Protocol", "");

                using var commandKey = Registry.CurrentUser.CreateSubKey(ProtocolKey + @"\shell\open\command");
                commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");

                Debug.WriteLine("Protocol handler registered successfully.");
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "Failed to register the ytdlp:// protocol. Please run the application as Administrator once to register the protocol handler.",
                    "Protocol Registration Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to register protocol handler: {ex.Message}",
                    "Protocol Registration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public static void Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(ProtocolKey, throwOnMissingSubKey: false);
                Debug.WriteLine("Protocol handler unregistered.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to unregister protocol: {ex.Message}");
            }
        }

        private static string GetExecutablePath()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var path = assembly.Location;

            if (string.IsNullOrEmpty(path))
            {
                path = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }

            return path;
        }
    }
}
