# YT Downloader Pro - Native Windows GUI Host

`YTDLPHost.exe` is a high-performance, single-instance companion desktop application for the YT Downloader Pro Chrome extension. It receives download commands via a custom URL protocol (`ytdlp://`), executes `yt-dlp` in a strictly sandboxed background environment, and provides a polished native Windows Presentation Foundation (WPF) GUI for queue management and real-time state tracking.

## ✨ Core Features

- **Advanced Process Sandboxing:** Executes `yt-dlp` in total stealth. Dynamically strips `WT_SESSION` variables and redirects input streams to bypass modern Windows Terminal hijacks, guaranteeing zero console window pop-ups.
- **UAC-Free Auto Updates:** The host application installs securely to `Program Files`, while the core media engines (`yt-dlp`, `ffmpeg`) are dynamically provisioned to `%LOCALAPPDATA%`. This allows the application to perform silent, background engine updates without triggering Administrator (UAC) prompts.
- **Smart IPC Payload Queue:** Bypasses standard Named Pipe size limits by utilizing a highly secure File IPC queue (`FileSystemWatcher`). The Named Pipe acts strictly as a "doorbell" to wake the UI, preventing race conditions and "Ghost Echo" duplicate downloads.
- **Anti-Duplicate Shield:** Intelligently analyzes incoming base64 command strings. It safely allows multiple resolutions of the same video but actively blocks identical command spams.
- **Bulletproof State Tracking:** Monitors independent memory states and file extensions to accurately report "Extracting Info...", "Downloading Video...", "Converting Audio...", and "Merging & Finalizing..." without UI flickering.
- **Mark of the Web (MotW) Stripper:** Automatically removes `Zone.Identifier` tags from downloaded engine binaries, preventing Windows Defender SmartScreen from forcing visible security transitions.
- **Premium Content Support:** Extracts and injects clean, BOM-free Base64 Netscape cookie files for age-restricted or premium-resolution content.

## 🏗️ Architecture

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
      ProtocolHandler.cs           # Global protocol registration
      SingleInstanceManager.cs     # Named Mutex + IPC Doorbell
      YtDlpRunner.cs               # Process isolation & regex stream parser
      TrayIconService.cs           # System tray & toast notifications
    Converters/
      StatusToColorConverter.cs    # Status -> Color brush converters
      ProgressToVisibilityConverter.cs # Progress -> Visibility converters
    ViewModels/
      MainViewModel.cs             # Queue management & dependency provisioning
      DownloadItemViewModel.cs     # Per-item VM
    App.xaml / App.xaml.cs         # Application bootstrap & IPC watcher
    MainWindow.xaml / .cs          # Main window (Dark Theme)
```

## ⚙️ Prerequisites

- Windows 10 (1903+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (If not bundled via self-contained publish)

## 🚀 Installation

The application is deployed via a compiled Windows Installer (`.exe`), which manages protocol registration and system-wide paths.

1. Download the latest `YTDownloaderPro_Setup_v6.0.0.exe` release.
2. Run the installer. 
3. The automated setup will:
   - Install the host application securely in `C:\Program Files\YT Downloader Pro`.
   - Register the `ytdlp://` protocol in the `HKLM` system registry.
   - Boot the app to silently fetch the latest `yt-dlp` and `ffmpeg` binaries into `%LOCALAPPDATA%\YTDownloaderProEngine`.

## 🛠️ Building from Source

### Step 1: Compile the C# Application
**With Visual Studio:**
1. Open `YTDLPHost.sln`.
2. Set configuration to `Release | Any CPU`.
3. Right-click project → **Publish** to a local folder (e.g., `publish\`).

**With CLI:**
```cmd
cd YTDLPHost
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o ..\publish
```

### Step 2: Compile the Installer
1. Install [Inno Setup 6+](https://jrsoftware.org/isinfo.php).
2. Open `setup.iss` in the root directory.
3. Verify the `[Files]` source paths match your local `publish` output directory.
4. Click **Build → Compile** (Ctrl+F9).
5. The final `.exe` installer will be generated in the `Output/` folder.

## 🔍 Process Flow Lifecycle

1. **Browser Trigger**: The Chrome extension builds a `ytdlp://<base64_command>||<base64_cookies>` URL and calls it.
2. **OS Hand-off**: Windows Native Protocol Handler intercepts the request and launches `YTDLPHost.exe "%1"`.
3. **IPC Routing**: If an instance is already running, the new instance drops the URL payload into `%LOCALAPPDATA%\YT Downloader Pro\Payloads`, pings the primary instance's Named Pipe doorbell, and exits instantly.
4. **Validation**: The primary instance wakes up, reads the file queue, decodes the Base64 command, and runs it through the Anti-Duplicate Shield and Path Security validators.
5. **Sandboxed Execution**: Spawns `yt-dlp.exe` with detached input/output streams and scrubbed terminal environment variables.
6. **Live Data Binding**: The `YtDlpRunner` regex engine scans the CLI standard output to throttle UI thread updates for maximum application performance.

## 🗑️ Uninstallation

The custom Inno Setup uninstaller ensures zero ghost files are left behind.
Go to **Settings > Apps > Installed Apps** and uninstall **YT Downloader Pro**. 
The uninstaller will automatically delete the `Program Files` host, unregister the Registry protocol, and surgically wipe the dynamic `%LOCALAPPDATA%` engine and payload queues.

## 📄 License

Companion application for YT Downloader Pro Chrome Extension.
