# Jellyfin Music 単体インストーラー 0.6.3-preview.1

Setup-x64.exeを実行すると、現在のユーザーの `%LOCALAPPDATA%\Programs\JellyfinMusic` へ本体・.NET・libmpv再生エンジン・タスクバー実行部品を導入します。Windhawkの別導入、開発環境、通常の管理者権限は不要です。スタートメニューからJellyfin Musicを起動してください。

タスクバー表示はインストーラー版で初期オン。本体設定でオフにできます。表示オンでUI配置済みの場合は×で常駐し、曲名クリックで本体へ復帰します。完全終了は設定から行います。本体終了時には同梱エンジンを停止します。自動起動は登録しません。

初版は実機確認済みtaskbar.dllのPDB GUID `23d06b07-e6aa-07e4-e886-6171fca13f62` / age 1が対象です。検証PCはWindows 11 26H1 build 28000.3365。OSの表示バージョンだけで互換と判断しません。未知の内部版ではタスクバー連携を開始せず、メイン画面は使用可能です。全Windows 11対応を意味しません。

同梱WindhawkはExplorerのみを対象とし、UI・開発ツール・Microsoftのシンボル取得用DLLを含めません。既存の別Windhawkが起動している場合は干渉せず、アプリ内で競合を表示します。別Windhawkの設定やModを変更しません。

更新は同じインストーラーを実行します。本体へ終了を要求し、同梱エンジンを停止してから置換します。終了できない場合は更新を中断します。Explorerを強制終了・再起動しません。

Windowsの「インストールされているアプリ」からアンインストールできます。先に本体と同梱エンジンを終了し、専用の実行部品とエンジン状態を除去します。接続情報・画像キャッシュの `%LOCALAPPDATA%\JellyfinMusic` は保持します。消去したい場合は本体設定で先に消去してください。

署名証明書は未設定です。署名付きリリースと同等の信頼表示にはなりません。

## 再ビルド

`build.ps1` に .NET SDKのdotnet.exe、Windhawk Portable 1.7.3のパス、このプロジェクトの0.3.1 DLL、公式Windhawk v1.7.3ソースZIP、.NET通知ファイルのディレクトリ、Inno Setup 7.1.1のISCC.exe、native配下の手順で作ったPlaybackBundle、新規ステージング先、出力先を渡します。ビルドは既存ステージングの削除を行わず、新規ディレクトリを要求します。

`JellyfinMusic.iss` がユーザー単位の導入・更新・削除を定義します。`taskbar-symbol-cache.ini` は認証情報を含まないWindowsシンボル名と相対アドレスのキャッシュです。`.NET`とWindhawkの開発ツールはビルド時だけ必要です。

ライセンス: `THIRD-PARTY-NOTICES.md` と `licenses/`。インストーラーに対応ソースも同梱します。

実機検証の結果は `VALIDATION.md` に記録します。

再生用ネイティブ部品は固定ソースからビルドし、対応するソースZIP全9件と再ビルド手順を同梱します。Jellyfin SDK等の管理ライブラリの表示はlicenses/managed、ネイティブ側はlicenses/PLAYBACK-NOTICES.mdを参照してください。原音Direct Playのみで、曲の切り替え・シークにはlibmpv、状態通知には公式Jellyfin SDKを使用します。


ネイティブPlaybackBundleの作成後、dotnet restore --locked-modeで本体依存を復元し、python collect-managed.py <project.assets.json> <NuGetパッケージフォルダー> <PlaybackBundle> を実行してください。NuGetメタデータに固定されたソースから表示を取得し、管理ライブラリの資料を追加してマニフェストを更新します。ビルド専用にPythonも必要です。
