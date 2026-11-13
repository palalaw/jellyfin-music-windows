#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppBuildVersion
  #error AppBuildVersion is required
#endif
[Setup]
AppId={{A4BD3AD6-C670-4CBC-A710-3865BB76DA2A}
AppName=Jellyfin Music
AppVersion={#AppBuildVersion}
AppPublisher=Jellyfin Music contributors
DefaultDirName={localappdata}\Programs\JellyfinMusic
DefaultGroupName=Jellyfin Music
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=JellyfinMusic-{#AppBuildVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\JellyfinMusic.exe
LicenseFile={#PayloadDir}\licenses\GPL-3.0.txt
InfoBeforeFile=install-info.txt
DisableProgramGroupPage=yes
SetupLogging=yes
[Languages]
Name: japanese; MessagesFile: compiler:Languages\Japanese.isl
[Tasks]
Name: desktopicon; Description: デスクトップにショートカットを作成する; Flags: unchecked
[Files]
Source: {#PayloadDir}\*; DestDir: {app}; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: {group}\Jellyfin Music; Filename: {app}\JellyfinMusic.exe
Name: {autodesktop}\Jellyfin Music; Filename: {app}\JellyfinMusic.exe; Tasks: desktopicon
[Run]
Filename: {app}\JellyfinMusic.exe; Description: Jellyfin Musicを起動; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: filesandordirs; Name: {app}\TaskbarRuntime\AppData
[Code]
function StopInstalledPlayer(): Boolean;
var Code: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\JellyfinMusic.exe')) then
    Result := Exec(ExpandConstant('{app}\JellyfinMusic.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopInstalledPlayer() then
    Result := 'Jellyfin Musicの設定から「完全終了」を選び、再試行してください。Explorerは再起動しません。';
end;
function InitializeUninstall(): Boolean;
begin
  Result := StopInstalledPlayer();
  if not Result then
    MsgBox('Jellyfin Musicの設定から「完全終了」を選び、アンインストールを再実行してください。', mbError, MB_OK);
end;
