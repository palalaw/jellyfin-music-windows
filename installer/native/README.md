# Playback native build

Run on Windows x64 with MSYS2 UCRT64 GCC, pkgconf, make; Python 3, Meson 1.12.1, Ninja 1.13.2, Jinja2 3.1.6, MarkupSafe 3.0.4. GCC and MinGW are unmodified upstream build tools. The shipped compiler-runtime license exceptions apply; see licenses/native/toolchain.

1. Install the Python tools into a dedicated directory using pip --target.
2. Run `python fetch-sources.py C:/build/playback`. This verifies SHA256 and extracts the accompanying source archives (or downloads missing ones).
3. Run `./build-native.ps1 -Root C:/build/playback -MsysRoot C:/msys64 -PythonTools C:/build/python-tools`.
4. Run `./package-native.ps1 -Root C:/build/playback -MsysRoot C:/msys64 -Destination C:/build/PlaybackBundle`.

Sources are unmodified. Only build options are selected. FFmpeg encoders, muxers and capture devices are disabled; decoders/demuxers remain enabled. TLS uses Windows Schannel. Optional GPU/rendering/scripting libraries are disabled; libass and libplacebo are required mpv build dependencies. The C ABI remains libmpv v2. The app uses WASAPI and Direct Play only.

All nine source archives, including fast_float and Vulkan header submodules, are supplied beside this file. The manifest pins archive hashes, version and origin. `licenses/native` contains component notices, build configuration, toolchain versions and native DLL imports. The resulting DLL can be replaced by a compatible rebuilt libmpv; there is no signature or checksum enforcement at application runtime. The installer validates its input bundle only.

For building from the accompanying archives, copy the .zip files into the Root directory before step 2. Keep them available when redistributing the installer. App source and build scripts are separately included under sources/jellyfin-windows-player.
