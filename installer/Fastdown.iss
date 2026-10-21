#ifndef PackageRoot
  #error PackageRoot is required
#endif
#ifndef AppVersion
  #define AppVersion "1.7.0"
#endif
[Setup]
AppId={{58F597F7-44F6-4E85-BD36-22C52B33C87D}
AppName=Fastdown
AppVersion={#AppVersion}
AppPublisher=Fastdown
AppPublisherURL=https://github.com/flamerood/fastdown
AppUpdatesURL=https://github.com/flamerood/fastdown/releases/latest
DefaultDirName={localappdata}\Programs\Fastdown
DefaultGroupName=Fastdown
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=Fastdown-Setup
SetupIconFile=..\src\Fastdown.App\assets\akis.ico
UninstallDisplayIcon={app}\Fastdown\Fastdown.exe
AppMutex=Local\Akis.DownloadManager
CloseApplications=no
RestartApplications=no
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"

[Files]
Source: "{#PackageRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\Fastdown"; Filename: "{app}\Fastdown\Fastdown.exe"
Name: "{group}\Tarayıcı eklentisi kurulumu"; Filename: "{app}\Kurulum-Rehberi.html"
Name: "{autodesktop}\Fastdown"; Filename: "{app}\Fastdown\Fastdown.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Fastdown"; ValueType: string; ValueName: "InstallRoot"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Run]
Filename: "{app}\Fastdown\Fastdown.BrowserHost.exe"; Parameters: "--register"; Flags: runhidden waituntilterminated
Filename: "{app}\Fastdown\Fastdown.exe"; Description: "Fastdown'ı aç"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateRun
Filename: "{app}\Kurulum-Rehberi.html"; Description: "Tarayıcı eklentisi kurulumunu göster"; Flags: shellexec postinstall skipifsilent unchecked
Filename: "{app}\Fastdown\Fastdown.exe"; Flags: nowait; Check: IsUpdateRun

[UninstallRun]
Filename: "{app}\Fastdown\Fastdown.BrowserHost.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RemoveBrowserBridge"

[Code]
function IsUpdateRun: Boolean;
begin
  Result := ExpandConstant('{param:FASTDOWNUPDATE|0}') = '1';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    // A missing browser bridge must be visible in the installer log.
    if not Exec(ExpandConstant('{app}\Fastdown\Fastdown.BrowserHost.exe'), '--check-install', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Fastdown kurulum dosyaları doğrulanamadı.');
  end;
end;
