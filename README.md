# YT Downloader Pro - Native Windows GUI Host

`YTDLPHost.exe` is a single-instance companion desktop application that receives download commands from the YT Downloader Pro Chrome extension via a custom URL protocol (`ytdlp://`), executes `yt-dlp` silently (no terminal window), and displays a polished native GUI with download queue management and real-time progress tracking.

## Features

- **Protocol Handler**: Self-registers the `ytdlp://` URL protocol in Windows Registry
- **Single Instance**: Uses named mutex + named pipes to enforce only one running instance
- **Silent Execution**: Runs `yt-dlp.exe` with `CreateNoWindow = true` — absolutely no terminal popup
- **Real-Time Progress**: Parses yt-dlp stdout live and updates progress bars, speed, and ETA
- **Dark Theme**: YouTube-inspired dark UI with specified color palette
- **System Tray**: Minimize to tray with Windows toast notifications on download completion
- **Queue Management**: Sequential download processing with status tracking
- **Cookie Support**: Handles Base64-encoded Netscape cookie files for age-restricted content

## Architecture

```
YTDLPHost/
  YTDLPHost.sln                    # Solution file
  YTDLPHost/
    YTDLPHost.csproj               # .NET 8 WPF project
    Assets/
      icon.ico                     # Application icon
    Models/
      DownloadTask.cs              # Download task model + enum
    Services/
      ProtocolHandler.cs           # Registry registration
      SingleInstanceManager.cs     # Mutex + Named Pipe server/client
      YtDlpRunner.cs               # yt-dlp process execution & parsing
      TrayIconService.cs           # System tray & toast notifications
    Converters/
      StatusToColorConverter.cs    # Status -> Color brush converters
      ProgressToVisibilityConverter.cs # Progress -> Visibility converters
    ViewModels/
      MainViewModel.cs             # Main queue management VM
      DownloadItemViewModel.cs     # Per-item VM
    App.xaml / App.xaml.cs         # Application bootstrap
    MainWindow.xaml / .cs         # Main window (dark theme)
```

## Prerequisites

- Windows 10 (1903+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or SDK to build)
- `yt-dlp.exe` and `ffmpeg.exe` available in PATH (run `setup.bat` first)

## Building

### Option 1: Build with Visual Studio 2022
1. Open `YTDLPHost.sln` in Visual Studio 2022
2. Select `Release | Any CPU`
3. Build → Build Solution (Ctrl+Shift+B)
4. Output: `YTDLPHost\bin\Release\net8.0-windows\YTDLPHost.exe`

### Option 2: Build with CLI
```cmd
cd YTDLPHost
dotnet build -c Release
```

### Option 3: Publish Single File
```cmd
cd YTDLPHost
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```
Output will be a single `YTDLPHost.exe` in:
`bin\Release\net8.0-windows\win-x64\publish\YTDLPHost.exe`

## Installation

### Step 1: Install Dependencies
Run the existing `setup.bat` to install yt-dlp, ffmpeg, and configure PATH:
```cmd
setup.bat
```

### Step 2: Place YTDLPHost.exe
Copy the built `YTDLPHost.exe` to a permanent location:
```cmd
mkdir C:\bin
copy YTDLPHost.exe C:\bin\
```

### Step 3: Register Protocol Handler
The app auto-registers the `ytdlp://` protocol on first launch. To manually register:
1. Run `YTDLPHost.exe` once normally — it will write registry keys automatically
2. Or use the Settings (gear icon) → "Re-register Protocol Handler"

### Step 4: Update Chrome Extension
Update the extension's protocol target from `launcher.bat` to `YTDLPHost.exe`. The app handles the same `ytdlp://` URL format as the old launcher.

## How It Works

1. **Extension sends URL**: When you click Download in YouTube, the extension builds a `ytdlp://<base64_command>||<base64_cookies>` URL and navigates to it
2. **Protocol handler**: Windows launches `YTDLPHost.exe "%1"` with the full URL
3. **Single instance check**: If already running, the new instance forwards the URL via named pipe and exits
4. **URL decoding**: Base64-decodes the command and optional cookies
5. **Silent execution**: Spawns `yt-dlp.exe` with no window, capturing stdout/stderr
6. **Progress parsing**: Parses real-time output for percentage, speed, and ETA
7. **Notification**: Shows Windows toast notification when complete

## URL Protocol Format

```
ytdlp://<base64_command>
ytdlp://<base64_command>||<base64_cookies>
```

The extension Base64-encodes the UTF-8 yt-dlp command string. If cookies are included, they are appended after `||` as a Base64-encoded Netscape cookie file.

## Error Handling

| Scenario | Behavior |
|----------|----------|
| yt-dlp not found | Dialog: "yt-dlp not found. Please run setup.bat first." |
| Invalid Base64 | Status bar: "Invalid download link received." |
| Download failure | Item marked Error, last 3 stderr lines shown |
| Cookie I/O error | Proceeds without cookies, logs warning |
| Empty queue | Shows centered illustration with instructions |

## Uninstallation

1. Click Settings (gear icon) → "Unregister Protocol Handler" to remove registry keys
2. Delete `C:\bin\YTDLPHost.exe`

## License

Companion application for YT Downloader Pro Chrome Extension.
