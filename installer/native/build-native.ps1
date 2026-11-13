param([Parameter(Mandatory=$true)][string]$Root,[string]$MsysRoot="C:/msys64",[string]$PythonTools="$PSScriptRoot/python-tools")
$ErrorActionPreference='Stop'
$rootPath=(Resolve-Path $Root).Path.Replace('\','/')
$prefix="$rootPath/prefix"
$env:PATH="$MsysRoot/ucrt64/bin;$MsysRoot/usr/bin;$PythonTools/bin;$env:PATH"
$env:PYTHONPATH="$PythonTools"
$env:C_INCLUDE_PATH="$rootPath/libplacebo/3rdparty/Vulkan-Headers/include"
$env:CC='gcc';$env:CXX='g++'
$env:PKG_CONFIG='pkgconf'
$env:PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
$env:TEMP="$rootPath/tmp";$env:TMP=$env:TEMP
New-Item -ItemType Directory -Force $env:TEMP,$prefix | Out-Null
function RunMeson([string]$name,[string[]]$options){
 $libraryType = if ($name -eq 'mpv') { 'shared' } else { 'static' }
 & python -m mesonbuild.mesonmain setup "$rootPath/$name-build" "$rootPath/$name" "--prefix=$prefix" '--libdir=lib' '--buildtype=release' "--default-library=$libraryType" '-Dauto_features=disabled' '--wrap-mode=nodownload' @options
 if($LASTEXITCODE){throw "Configure $name"}
 & python -m mesonbuild.mesonmain compile -C "$rootPath/$name-build" -j 6
 if($LASTEXITCODE){throw "Compile $name"}
 & python -m mesonbuild.mesonmain install -C "$rootPath/$name-build"
 if($LASTEXITCODE){throw "Install $name"}
}
RunMeson freetype @('-Dharfbuzz=disabled','-Dzlib=disabled')
RunMeson harfbuzz @('-Dfreetype=enabled','-Dtests=disabled','-Dutilities=disabled')
RunMeson fribidi @('-Ddocs=false','-Dbin=false','-Dtests=false')
RunMeson libass @('-Ddirectwrite=enabled')
RunMeson libplacebo @('-Ddemos=false','-Dtests=false')
$env:MSYS2_ARG_CONV_EXCL=$null
$env:PATH="$prefix/bin;$env:PATH"
$script="cd '$rootPath/ffmpeg' && ./configure --prefix='$prefix' --pkg-config=pkgconf --pkg-config-flags=--static --cc=gcc --cxx=g++ --target-os=mingw32 --arch=x86_64 --enable-static --disable-shared --disable-autodetect --enable-schannel --disable-x86asm --disable-programs --disable-doc --disable-encoders --disable-muxers --disable-devices --disable-debug && make -j6 && make install"
& "$MsysRoot/usr/bin/bash.exe" -c $script
if($LASTEXITCODE){Get-Content "$rootPath/ffmpeg/ffbuild/config.log" -Tail 100;throw 'FFmpeg build'}
RunMeson mpv @('--prefer-static','-Dc_link_args=-lstdc++','-Dgl=disabled','-Dwin32-threads=enabled','-Dlibmpv=true','-Dcplayer=false','-Dgpl=false','-Dwasapi=enabled','-Dlua=disabled','-Dbuild-date=false','-Dtests=false')





