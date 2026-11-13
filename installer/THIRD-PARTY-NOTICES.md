# 同梱物とソース

Jellyfin Musicの独自実装とインストーラー定義はGPL-3.0-onlyで提供します。許諾条件はGPL-3.0.txt。既存部品には元のライセンスが適用されます。

| 部品 | 版・由来 | 条件・ソース |
|---|---|---|
| Windhawk実行ホスト・エンジン | 公式1.7.3の未改変バイナリー。windhawk.exe、x64 helper、32/64 engineのみ | GPLv3。GPL-3.0.txt、sources/windhawk-v1.7.3-source.zip。同アーカイブにビルドプロジェクト、ビルドスクリプト、組み込みライブラリと各表示を含む |
| タスクバーDLL | 本プロジェクト0.3.1。SalytsのMITヘルパーから改変 | GPL-3.0-only、Taskbar-MIT.txt。対応ソースはsources/jellyfin-windows-player/taskbar-integration |
| libc++.whl / libunwind.whl | Windhawk 1.7.3が配布するランタイム | Apache-2.0 WITH LLVM-exception。LLVM.txt。MinGW由来部分の表示はMinGW-runtime.txt |
| .NET / WindowsDesktop | .NET 10 self-contained publish | DOTNET-LICENSE.txt、DOTNET-THIRD-PARTY-NOTICES.txt、WINDOWSDESKTOP-LICENSE.txt |
| Inno Setup | 7.1.1。インストーラー・アンインストーラーのランタイム | Inno-Setup.txt |

Windhawkの設定をアプリ専用にしています。バイナリーは改変していません。UI、コンパイラー、Microsoft DIA/SymSrv DLLは同梱しません。タスクバーシンボルは実機で得た特定PDB識別子のキャッシュだけを同梱します。認証情報・サーバーURL・実音源は含めません。

ソース由来:
- https://github.com/ramensoftware/windhawk/tree/v1.7.3
- https://github.com/Salyts/Taskbar-Fluent-Media-Player/tree/234aaa6f03a8c3ebf9c00ab972fd62046c28dd64
- https://github.com/llvm/llvm-project
- https://github.com/dotnet/runtime
- https://github.com/dotnet/wpf
- https://github.com/jrsoftware/issrc/tree/is-7_1_1

インストール先のsourcesに本体、連携DLL、ビルドスクリプトを含めています。Windhawkはその公式ビルド手順、タスクバーDLLはWindhawk 1.7.3のローカルModビルドを使用します。再ビルドには.NET SDK 10、Windhawkの開発用配布物、Inno Setupを使用します。これらはエンドユーザーの導入要件ではありません。

## 0.6 再生基盤

libmpv 0.42.0、FFmpeg 8.0.1と必須の依存部品は固定ソースからビルドします。構成・ライセンス原文・著作権表示・ソース対応表はlicenses/PLAYBACK-NOTICES.mdとlicenses/native、対応ソース全9件と手順はsources/playbackに同梱します。

Jellyfin.Sdk 2026.10.12、Microsoft.Kiota 2.1.2、Microsoft.IO.RecyclableMemoryStream 3.0.1、Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0はMIT、Std.UriTemplate 2.0.12はApache-2.0。ライセンス原文と各NuGetメタデータをlicenses/managedに収録します。これらは未改変のNuGetパッケージを使用します。

