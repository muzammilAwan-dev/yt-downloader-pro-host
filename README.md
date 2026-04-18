# YT Downloader Pro - Native Windows GUI Host

`YTDLPHost.exe` is a single-instance companion desktop application that receives download commands from the YT Downloader Pro Chrome extension via a custom URL protocol (`ytdlp://`), executes `yt-dlp` silently (no terminal window), and displays a polished native GUI with download queue management, bulletproof state tracking, and real-time progress.

## Features

- **Automated Installer**: Deploys via a professional Inno Setup installer that automatically downloads the latest `yt-dlp` and `ffmpeg` engine directly to `Program Files`.
- **Bulletproof State Machine**: Intelligently tracks file extensions and independent memory states to accurately report "Downloading Video...", "Downloading Audio...", "Downloading Subtitles...", or "Downloading Thumbnail..." without flickering, even on complex playlist or split-container downloads.
- **Protocol Handler**: Auto-registers the `ytdlp://` URL protocol system-wide (HKLM) in the Windows Registry.
- **Single Instance Engine**: Uses named mutex + named pipes to enforce only one running instance, funneling new clicks directly into the existing queue.
- **Silent Execution**: Runs `yt-dlp.exe` with `CreateNoWindow = true` — absolutely no terminal popups.
- **High-Performance UI**: Utilizes `RegexOptions.Compiled`, 100ms UI throttling, and `DispatcherPriority.Background` to keep the UI buttery smooth during high-speed, multi-threaded downloads.
- **Dark Theme**: YouTube-inspired dark UI with custom color palette and fluid animations.
- **Dual Minimize**: Minimize to the taskbar or click the custom minimize button to hide directly to the System Tray.
- **Queue Management**: Intelligent concurrency limits (max 3) with full pause, resume, cancel, and clear functionality.
- **Cookie Support**: Handles Base64-encoded Netscape cookie files for age-restricted content and premium resolutions.

## Architecture

```text
YTDLPHost/
  YTDLPHost.sln                    # Solution file
  setup.iss                        # Inno Setup compilation script
  YTDLPHost/
    YTDLPHost.csproj               # .NET 8 WPF project
    Assets/
      icon.ico                     # Application icon
    Models/
      DownloadTask.cs              # Download task model + enum
    Services/
      ProtocolHandler.cs           # Registry registration
      SingleInstanceManager.cs     # Mutex + Named Pipe server/client
      YtDlpRunner.cs               # Bulletproof yt-dlp execution & parsing
      TrayIconService.cs           # System tray & toast notifications
    Converters/
      StatusToColorConverter.cs    # Status -> Color brush converters
      ProgressToVisibilityConverter.cs # Progress -> Visibility converters
    ViewModels/
      MainViewModel.cs             # Main queue management VM
      DownloadItemViewModel.cs     # Per-item VM
    App.xaml / App.xaml.cs         # Application bootstrap
    MainWindow.xaml / .cs          # Main window (dark theme)
```

## Prerequisites

- Windows 10 (1903+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (If not bundled)

## Installation

We have deprecated the old `setup.bat` script in favor of a clean, automated Windows Installer.

1. Download the latest `YTDownloaderPro_Setup.exe` release.
2. Run the installer. 
3. The installer will automatically:
   - Place the host application in `C:\Program Files\YT Downloader Pro`
   - Download the absolute latest `yt-dlp.exe` and `ffmpeg.exe` dependencies in the background.
   - Register the `ytdlp://` protocol securely in the system registry.
   - Create desktop and start menu shortcuts.

*Note: You no longer need to manually copy files or mess with your System PATH.*

## Building from Source

### Step 1: Compile the C# Application
**With Visual Studio:**
1. Open `YTDLPHost.sln`
2. Select `Release | Any CPU`
3. Right-click project → **Publish** to a local folder (e.g., `publish\YTDLPHost.exe`)

**With CLI:**
```cmd
cd YTDLPHost
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o ..\publish
```

### Step 2: Compile the Installer
1. Install [Inno Setup](https://jrsoftware.org/isinfo.php).
2. Open the `setup.iss` file located in the root directory.
3. Verify the `[Files]` source paths match your publish directory.
4. Click **Build → Compile** (Ctrl+F9).
5. The final installer will be generated in the `Output/` folder.

## How It Works

1. **Extension sends URL**: When you click Download in YouTube, the Chrome extension builds a `ytdlp://<base64_command>||<base64_cookies>` URL and navigates to it.
2. **Protocol handler**: Windows natively intercepts this and launches `YTDLPHost.exe "%1"`.
3. **Single instance check**: If the app is already open, the new instance forwards the URL via a named pipe to the existing window and exits instantly.
4. **Validation & Decoding**: The app decodes the Base64 command, validates it against malicious path injections, and extracts temporary cookies.
5. **Execution**: Spawns `yt-dlp.exe` bound to background threads.
6. **Live Parsing**: The bulletproof regex engine scans standard output to extract resolutions, track independent memory states for split-audio/video downloads, and throttle UI updates for maximum performance.

## Uninstallation

Because the app is now fully managed by Windows, simply go to **Settings > Apps > Installed Apps** (or Control Panel) and uninstall **YT Downloader Pro**. The custom uninstaller will safely clean up all executables, dynamic dependencies, and registry keys.

## License

Companion application for YT Downloader Pro Chrome Extension.
How does this look? It reads like a top-tier open-source GitHub repository now!
