# 再生エンジン・依存部品

本配布のlibmpvは固定した上流ソースからビルド。上流ソースの変更なし。構成はsources/playback/build-native.ps1、対応ソースは同フォルダーのZIP全9件、取得元・版・SHA256はsources.json。生成DLLは動的に読み込み、互換DLLへの差し替えを妨げません。

| 部品 | 版 | 適用ライセンス・表示 |
|---|---|---|
| mpv | 0.42.0、gpl=false | LGPL-2.1-or-later。native/mpv/Copyright、LICENSE.LGPL |
| FFmpeg | 8.0.1、GPL/nonfree未指定 | LGPL-2.1-or-later。native/ffmpegのLICENSE・COPYING群。構成はffmpeg-config.* |
| libass | 0.17.5 | ISC。native/libass/COPYING |
| FreeType | 2.14.1 | FreeType Licenseを選択。native/freetype/FTL.TXT。Portions of this software are copyright © 1996-2025 The FreeType Project (www.freetype.org). All rights reserved. |
| HarfBuzz | 12.2.0 | MIT系。native/harfbuzz/COPYING |
| FriBidi | 1.0.18 | LGPL-2.1-or-later。native/fribidi/COPYING |
| libplacebo | 7.387.0 | LGPL-2.1-or-later。native/libplacebo/LICENSE |
| fast_float | 97b54ca9e75f5303507699d27c6b4f4efe4641a1 | MITを選択。native/fast_float/LICENSE-MIT |
| Vulkan-Headers | 74d8a6cb930c68ef617b202c3ff3c59d919e086b | Apache-2.0 / MIT（各ファイル）。native/vulkan-headers/LICENSE*。ビルド時ヘッダーのみ |
| GCC runtime / MinGW-w64 | toolchain-packages.txt参照 | GCC GPL-3.0-or-later WITH GCC-exception-3.1、MinGW各許諾。native/toolchainの原文を同梱 |

各ソースアーカイブ内のファイル別の著作権・許諾も維持しています。上記は代表表示であり原文を置換しません。GPLのみの機能、外部の変換実行ファイル、音源保存機能は追加しません。音源の再エンコードや品質を落とすフォールバックは行いません。

インストール先のsources/playbackには再ビルド可能な対応ソースと設定が含まれます。インストーラーを再配布する場合はこの同梱資料を保持してください。

This software is based in part on the work of the Independent JPEG Group. FFmpeg内の該当ファイルに本プロジェクトによる変更はありません。原著作権表示・許諾条件はnative/ffmpeg/IJG-source-notice.cと対応ソースに収録します。
