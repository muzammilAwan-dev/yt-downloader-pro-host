[Setup]
AppId={{95CAE36F-FAB4-4DDB-BBE1-7F9C354125A2}
AppName=YT Downloader Pro
AppVersion=6.0.0
AppPublisher=muzammilAwan-dev
AppCopyright=Copyright (C) 2026 muzammilAwan-dev

; --- INSTALLATION PATHS ---
; We can safely use autopf (Program Files) now because the engines are dynamically downloaded to LocalAppData
DefaultDirName={autopf}\YT Downloader Pro
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
PrivilegesRequired=admin

; --- OUTPUT SETTINGS ---
OutputDir=Output
OutputBaseFilename=YTDownloaderPro_Setup_v6.0.0
SetupIconFile=Assets\icon.ico
UninstallDisplayIcon={app}\YTDLPHost.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; --- THE CORE APPLICATION ---
Source: "publish\YTDLPHost.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Assets\icon.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
; --- SHORTCUTS ---
Name: "{autoprograms}\YT Downloader Pro"; Filename: "{app}\YTDLPHost.exe"
Name: "{autodesktop}\YT Downloader Pro"; Filename: "{app}\YTDLPHost.exe"; Tasks: desktopicon

[Registry]
; --- PROTOCOL REGISTRATION ---
Root: HKLM; Subkey: "Software\Classes\ytdlp"; ValueType: string; ValueName: ""; ValueData: "URL:YT Downloader Protocol"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\ytdlp"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\ytdlp\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\YTDLPHost.exe"" ""%1"""; Flags: uninsdeletekey

[UninstallDelete]
; --- CLEANUP DYNAMIC FILES ---
; The installer will strictly delete the LocalAppData engine and payload folders during uninstallation
Type: filesandordirs; Name: "{localappdata}\YTDownloaderProEngine"
Type: filesandordirs; Name: "{localappdata}\YT Downloader Pro"
Type: dirifempty; Name: "{app}"