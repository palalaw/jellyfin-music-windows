param([Parameter(Mandatory=$true)][string]$Root,[string]$MsysRoot='C:/msys64',[Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Destination){throw 'Use a new bundle directory'}
New-Item -ItemType Directory -Force "$Destination/runtime","$Destination/licenses/native","$Destination/sources/playback" | Out-Null
Copy-Item "$Root/prefix/bin/libmpv-2.dll" "$Destination/runtime/"
$done=@{};$dependencyLog=@()
do {
 $pending=@(Get-ChildItem "$Destination/runtime" -Filter '*.dll' | Where-Object {!$done.ContainsKey($_.Name)})
 foreach($file in $pending){
  $done[$file.Name]=$true
  $imports=& "$MsysRoot/ucrt64/bin/objdump.exe" -p $file.FullName | Select-String 'DLL Name:'
  if($LASTEXITCODE){throw 'Cannot inspect native dependencies'}
  foreach($line in $imports){
   $name=($line.ToString() -split 'DLL Name:')[1].Trim()
   $dependencyLog+=[pscustomobject]@{file=$file.Name;imports=$name}
   if(Test-Path "$Destination/runtime/$name"){continue}
   if(Test-Path "$MsysRoot/ucrt64/bin/$name"){Copy-Item "$MsysRoot/ucrt64/bin/$name" "$Destination/runtime/"}
   elseif(!(Test-Path "$env:WINDIR/System32/$name") -and $name -notlike 'api-ms-win-*' -and $name -notlike 'ext-ms-win-*'){throw "Unresolved native dependency: $name"}
  }
 }
}while($pending.Count)
$dependencyLog | ConvertTo-Json | Set-Content "$Destination/licenses/native/imports.json" -Encoding utf8
foreach($archive in Get-ChildItem $Root -Filter '*.zip'){Copy-Item $archive.FullName "$Destination/sources/playback/"}
Copy-Item "$PSScriptRoot/sources.json","$PSScriptRoot/fetch-sources.py","$PSScriptRoot/build-native.ps1","$PSScriptRoot/package-native.ps1","$PSScriptRoot/README.md" "$Destination/sources/playback/"
$components=@('mpv','ffmpeg','libass','freetype','harfbuzz','fribidi','libplacebo','fast_float','vulkan-headers')
foreach($component in $components){
 $target="$Destination/licenses/native/$component"
 New-Item -ItemType Directory -Force $target | Out-Null
 foreach($file in Get-ChildItem "$Root/$component" -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|Copyright|NOTICE|AUTHORS)' -or $_.Directory.Name -eq 'LICENSES' }) {
  $relative=[IO.Path]::GetRelativePath("$Root/$component",$file.FullName)
  $destinationFile=Join-Path $target $relative
  New-Item -ItemType Directory -Force (Split-Path $destinationFile -Parent) | Out-Null
  Copy-Item $file.FullName $destinationFile
 }
}
Copy-Item "$Root/ffmpeg/libavcodec/jrevdct.c" "$Destination/licenses/native/ffmpeg/IJG-source-notice.c"
Copy-Item "$Root/freetype/docs/FTL.TXT" "$Destination/licenses/native/freetype/"
Copy-Item "$Root/freetype/docs/GPLv2.TXT" "$Destination/licenses/native/freetype/"
Copy-Item "$MsysRoot/ucrt64/share/licenses" "$Destination/licenses/native/toolchain" -Recurse
& "$MsysRoot/usr/bin/pacman.exe" -Q | Set-Content "$Destination/licenses/native/toolchain-packages.txt" -Encoding utf8
Copy-Item "$Root/ffmpeg/ffbuild/config.mak" "$Destination/licenses/native/ffmpeg-config.mak"
Copy-Item "$Root/ffmpeg/config.h" "$Destination/licenses/native/ffmpeg-config.h"
Copy-Item "$PSScriptRoot/PLAYBACK-NOTICES.md" "$Destination/licenses/"
$manifest=@(Get-ChildItem $Destination -Recurse -File | ForEach-Object {@{path=[IO.Path]::GetRelativePath($Destination,$_.FullName);sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}})
$manifest | ConvertTo-Json -Depth 4 | Set-Content "$Destination/licenses/playback-manifest.json" -Encoding utf8
