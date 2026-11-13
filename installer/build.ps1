param(
    [Parameter(Mandatory=$true)][string]$Dotnet,
    [Parameter(Mandatory=$true)][string]$WindhawkPortable,
    [Parameter(Mandatory=$true)][string]$BridgeDll,
    [Parameter(Mandatory=$true)][string]$WindhawkSourceZip,
    [Parameter(Mandatory=$true)][string]$DotnetLicenses,
    [Parameter(Mandatory=$true)][string]$PlaybackBundle,
    [string]$ISCC='C:\Program Files\Inno Setup 7\ISCC.exe',
    [Parameter(Mandatory=$true)][string]$Staging,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
[xml]$applicationProject=Get-Content -LiteralPath (Join-Path $projectRoot 'main-window/MainWindow.csproj') -Raw
$applicationVersion=[string]$applicationProject.Project.PropertyGroup.Version
$PlaybackBundle=(Resolve-Path -LiteralPath $PlaybackBundle).Path
foreach($required in @('runtime/libmpv-2.dll','licenses/playback-manifest.json','sources/playback/build-native.ps1','sources/playback/sources.json','licenses/PLAYBACK-NOTICES.md')) {
    if(!(Test-Path -LiteralPath (Join-Path $PlaybackBundle $required))) {throw "Missing playback redistribution material: $required"}
}
$playbackManifest=Get-Content -LiteralPath (Join-Path $PlaybackBundle 'licenses/playback-manifest.json') -Raw | ConvertFrom-Json
foreach($entry in $playbackManifest) {
    $candidate=[IO.Path]::GetFullPath((Join-Path $PlaybackBundle $entry.path))
    $bundleRoot=[IO.Path]::GetFullPath($PlaybackBundle).TrimEnd('\')+'\'
    if(!$candidate.StartsWith($bundleRoot,[StringComparison]::OrdinalIgnoreCase)) {throw 'Invalid playback manifest path'}
    if((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $entry.sha256) {throw "Playback bundle mismatch: $($entry.path)"}
}
if(Test-Path -LiteralPath $Staging) {throw 'Staging must be a new directory; existing data will not be removed.'}
New-Item -ItemType Directory -Path $Staging | Out-Null
$Staging=(Resolve-Path -LiteralPath $Staging).Path
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination=(Resolve-Path -LiteralPath $Destination).Path
& $Dotnet publish (Join-Path $projectRoot 'main-window/MainWindow.csproj') -c Release -r win-x64 --self-contained true -o $Staging --no-restore --nologo
if($LASTEXITCODE -ne 0) {throw 'Application publish failed'}
function Copy-OwnedFile([string]$source,[string]$relative) {
    $target=Join-Path $Staging $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $PlaybackBundle 'runtime') -File) {Copy-OwnedFile $file.FullName $file.Name}
foreach($folder in @('licenses','sources')) {
    $base=Join-Path $PlaybackBundle $folder
    foreach($file in Get-ChildItem -LiteralPath $base -File -Recurse) {Copy-OwnedFile $file.FullName "$folder/$($file.FullName.Substring($base.Length+1))"}
}
foreach($file in @('windhawk.exe','windhawk-x64-helper.exe')) {Copy-OwnedFile (Join-Path $WindhawkPortable $file) "TaskbarRuntime/$file"}
foreach($arch in @('32','64')) {Copy-OwnedFile (Join-Path $WindhawkPortable "Engine/1.7.3/$arch/windhawk.dll") "TaskbarRuntime/Engine/1.7.3/$arch/windhawk.dll"}
foreach($file in @('libc++.whl','libunwind.whl')) {Copy-OwnedFile (Join-Path $WindhawkPortable "AppData/Engine/Mods/64/$file") "TaskbarRuntime/AppData/Engine/Mods/64/$file"}
Copy-OwnedFile $BridgeDll 'TaskbarRuntime/AppData/Engine/Mods/64/jellyfin-taskbar-0.3.1.dll'
Copy-OwnedFile (Join-Path $PSScriptRoot 'taskbar-symbol-cache.ini') 'TaskbarRuntime/AppData/Engine/ModsWritable/local@taskbar-jellyfin-player.ini'
@'
[Storage]
Portable=1
CompilerPath=Compiler
EnginePath=Engine\1.7.3
UIPath=UI
AppDataPath=AppData
'@ | Set-Content -LiteralPath (Join-Path $Staging 'TaskbarRuntime/windhawk.ini') -Encoding ascii
@'
[Storage]
Portable=1
AppDataPath=..\..\AppData\Engine
'@ | Set-Content -LiteralPath (Join-Path $Staging 'TaskbarRuntime/Engine/1.7.3/engine.ini') -Encoding ascii
@'
[Settings]
HideTrayIcon=1
DisableUpdateCheck=1
DontAutoShowToolkit=1
DisableToolkitHotkey=1
LoggingVerbosity=0
'@ | Set-Content -LiteralPath (Join-Path $Staging 'TaskbarRuntime/AppData/settings.ini') -Encoding ascii
@'
[Settings]
Include=explorer.exe
Exclude=*
InjectIntoCriticalProcesses=0
InjectIntoIncompatiblePrograms=0
InjectIntoGames=0
LoggingVerbosity=0
'@ | Set-Content -LiteralPath (Join-Path $Staging 'TaskbarRuntime/AppData/Engine/settings.ini') -Encoding ascii
@'
[Mod]
Disabled=0
LibraryFileName=jellyfin-taskbar-0.3.1.dll
LoggingEnabled=0
Include=explorer.exe
Exclude=
Architecture=x86-64
Version=0.3.1
SettingsChangeTime=1
[Settings]
enabled=1
'@ | Set-Content -LiteralPath (Join-Path $Staging 'TaskbarRuntime/AppData/Engine/Mods/local@taskbar-jellyfin-player.ini') -Encoding ascii
Copy-OwnedFile $WindhawkSourceZip 'sources/windhawk-v1.7.3-source.zip'
foreach($header in @('windhawk_api.h','windhawk_api_internal.h','windhawk_utils.h')) {
    Copy-OwnedFile (Join-Path $WindhawkPortable "Compiler/include/$header") "sources/jellyfin-windows-player/taskbar-integration/include/$header"
}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -File) {Copy-OwnedFile $file.FullName "licenses/$($file.Name)"}
foreach($file in Get-ChildItem -LiteralPath $DotnetLicenses -File) {Copy-OwnedFile $file.FullName "licenses/$($file.Name)"}
Copy-OwnedFile (Join-Path $projectRoot 'taskbar-integration/THIRD-PARTY-NOTICES.txt') 'licenses/Taskbar-MIT.txt'
Copy-OwnedFile (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') 'licenses/THIRD-PARTY-NOTICES.md'
foreach($folder in @('main-window','taskbar-integration','installer')) {
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File) {
        Copy-OwnedFile $file.FullName "sources/jellyfin-windows-player/$folder/$($file.Name)"
    }
}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -File) {Copy-OwnedFile $file.FullName "sources/jellyfin-windows-player/installer/licenses/$($file.Name)"}
Copy-OwnedFile (Join-Path $PSScriptRoot 'licenses/GPL-3.0.txt') 'sources/jellyfin-windows-player/LICENSE'
Copy-OwnedFile (Join-Path $PSScriptRoot 'README.md') 'README.md'
$manifest=Get-ChildItem -LiteralPath (Join-Path $Staging 'TaskbarRuntime') -Recurse -File | ForEach-Object {
    @{path=$_.FullName.Substring($Staging.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Staging 'licenses/runtime-manifest.json') -Encoding utf8
& python (Join-Path $PSScriptRoot 'verify-payload.py') $Staging
if($LASTEXITCODE -ne 0) {throw 'Payload verification failed'}
& $ISCC "/DPayloadDir=$Staging" "/DOutputDir=$Destination" "/DAppBuildVersion=$applicationVersion" (Join-Path $PSScriptRoot 'JellyfinMusic.iss')
if($LASTEXITCODE -ne 0) {throw 'Installer compilation failed'}


