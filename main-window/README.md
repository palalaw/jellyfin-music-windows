# Jellyfin Music 0.6.3-preview.1（開発版）

Windows 11向けのメイン画面実装です。タスクバー内UIは別の試験用DLLと連携します。単体インストーラー版は必要な実行エンジンを同梱します。アプリ名は仮称です。

## 機能

- Jellyfinへログインし、アルバム・曲・ジャケットを取得。
- アルバム一覧と、お気に入りアルバム一覧。アルバム詳細からお気に入りを追加・解除し、Jellyfinに反映。
- アルバム名・アーティスト名でアルバムを検索。独立した「全曲」「アーティスト」画面はありません。
- アルバム詳細の曲選択、キュー追加・削除、再生／一時停止、前／次、シーク、音量。
- アルバムループと1曲ループを別ボタンで選択。片方をオンにするともう片方はオフ。同じボタンを再度押すとループをオフにします。
- 実接続なしで見た目と操作を確認するデモモード（無音）。通常起動時は未接続です。

## ループの動作

画面上部で音楽ライブラリを選択できます。「すべての音楽ライブラリ」も選べます。アルバム・お気に入り・検索は選択したライブラリが対象です。切り替えても再生キューを保持します。

追加表示ボタンはありません。全アルバムをスクロールで閲覧でき、画面外の行はWPFが仮想化します。APIのページングは内部で自動処理します。

画面の戻るボタン、BrowserBackキー、マウスの戻るボタン（XButton1）、Alt+Leftに対応します。戻る際にライブラリ・検索条件・スクロール位置を復元します。

| モード | 自然に曲が終わったとき | 手動で次の曲を選んだとき |
|---|---|---|
| オフ | 次のキューへ進む。末尾で停止 | 選んだ曲へ移動 |
| 1曲 | 現在の曲を最初から再生 | 選んだ曲へ移り、1曲ループを保持 |
| アルバム | 現在の曲のアルバム内で次へ。最後は先頭へ | 選んだ曲のアルバムが対象になり、アルバムループを保持 |

ループ設定は曲・アルバム・キューの選択でリセットしません。混在キューでもアルバムループが別アルバムへ進むことはありません。部分的にキューへ追加した場合も、読み込み済みのアルバム全曲を対象にします。シャッフルとアルバムループを併用するとそのアルバム内だけで選びます。1曲ループは自然終端時にシャッフルより優先されます。アプリ終了後への設定保存は未実装です。

## 起動・接続

配布ZIP全体を展開し、`JellyfinMusic.exe`を起動してください。開発環境や.NETの別途導入は不要です。設定画面でサーバーURL・ユーザー名・パスワードを入力して「接続」を押します。URLには必要に応じてサーバーのサブパスも含めます。

接続先は`ServerProfile`として認証トークンと分離しています。接続先・ユーザー名・ユーザーID・デバイスID・認証トークンをWindows DPAPI（CurrentUser）で暗号化して保存し、次回起動時に再接続します。パスワードは保存しません。トークンが失効した場合は再ログインしてください。テスト用サーバーの情報はソース・配布物に埋め込んでいません。

タスクバー表示オフ、またはUI未配置の場合、×で完全終了します。タスクバー表示オンかつUI配置済みの場合、×でウィンドウを隠して常駐し、曲名クリックで復帰します。設定の「完全終了」で本体を終了します。自動起動・メディアキー・前回キュー復元・複数サーバー切り替えは未実装です。

ソース・ZIP版のタスクバー連携は `../taskbar-integration` の開発用DLLが必要です。単体インストーラーは `../installer` を参照してください。表示設定はpreferences.jsonへ保存します。この本体だけを起動してもExplorerへコードを読み込みません。試作DLLの配置・操作・解除は実機で確認しましたが、単体インストーラーを作成しました。導入試験、幅広いWindows版、DPI・複数モニター等の検証は別途記録します。詳細と既知の制約は同フォルダーのREADMEを参照してください。検証にはWindhawk Portableを使用しています。

## 再生方式

libmpvによる原音Direct Playと公式Jellyfin C# SDKによる再生情報取得・状態通知を使用します。独自HTTP中継とWPF MediaPlayerは削除しました。変換は行いません。比較・責務・検証結果・配布条件は [PLAYBACK-MIGRATION.md](PLAYBACK-MIGRATION.md) を参照してください。

0.6インストーラーは再生エンジンと対応ソース・ライセンス資料を同梱します。ソースからローカルで再生するには、ビルド出力と同じフォルダーに互換性のあるWindows x64版libmpv-2.dllとその実行依存DLLが必要です。installer/nativeに再ビルド手順があります。

## ローカル保存

保存先は %LOCALAPPDATA%\JellyfinMusic です。connection.binは接続情報全体をDPAPIで暗号化したファイル、artworkは画像キャッシュです。通常終了では認証トークンを失効させません。設定の「ログアウト・保存情報を削除」で保存情報を消去し、サーバーへログアウト要求を送ります。「画像キャッシュを削除」も利用できます。未接続時にも保存情報を削除できます。

配布ZIPを更新・移動しても保存情報を使用できます。ZIP版の削除だけでは設定は消えません。設定から消去するか、アプリ終了後に上記専用フォルダーを削除してください。テスト・描画モードは通常の保存情報を読み書きしません。

原音API仕様: https://typescript-sdk.jellyfin.org/interfaces/generated-client.AudioApiGetAudioStreamByContainerRequest.html
暗号化仕様: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope

## 開発・検証

Windows x64、.NET 10 SDK。

```powershell
dotnet restore MainWindow.csproj --configfile NuGet.Config
dotnet build MainWindow.csproj -c Release --no-restore
dotnet run --project MainWindow.csproj -c Release --no-build
dotnet publish MainWindow.csproj -c Release -r win-x64 --self-contained true -o publish
```

```powershell
dotnet bin/Release/net10.0-windows/JellyfinMusic.dll --self-test
dotnet bin/Release/net10.0-windows/JellyfinMusic.dll --render favorites.png favorites
dotnet bin/Release/net10.0-windows/JellyfinMusic.dll --render album.png album
```

`--integration-test report.json`は、実行プロセスに設定した`JFM_TEST_SERVER`、`JFM_TEST_USER`、`JFM_TEST_PASSWORD`を使います。テストアカウント専用です。サンプルアルバムのお気に入りを一時変更してfinallyで元に戻し、音量0で再生・一時停止・シークを検査します。認証情報や音声URLをレポートに出力しません。`--render-live image.png albums`は同じ環境変数で接続し、ウィンドウを表示せず描画します。

## ソース構成

`--ui-test report.json`で480枚のデータによるスクロール終端・仮想化・履歴復元を検査できます。`--library-test report.json`は上記のテスト環境変数を使い、ライブラリと各アルバムの取得を読み取り専用で検査します。

- `AlbumBrowser.cs` / `NavigationHistory.cs`：全件スクロールと閲覧履歴。
- `UiVerification.cs`：画面・履歴の自動検証。

- `JellyfinClient.cs`：認証、ページング、アルバム・曲・お気に入り・画像・音声のAPI。
- `Library.cs`：データ型とデモデータ。
- `PlaybackSession.cs`：UIや音声エンジンから独立したキュー・ループ状態。
- `AudioPlayback.cs`：音声エンジンとの連携。
- `MpvAudioEngine.cs` / `IAudioEngine.cs`：libmpvの制御と状態観測。
- `MainWindow.xaml` / `MainWindow.xaml.cs`：画面と操作。
- `SelfTest.cs` / `IntegrationTest.cs`：状態遷移・実サーバーの検証。

.NET/WPFランタイムを同梱する配布物にはlicensesの著作権・許諾・第三者表示も含めます。アプリのライセンスは上位のLICENSEを参照してください。新エンジンのバイナリ配布資料はインストーラーに同梱します。Symfoniumの素材は使用していません。

## 0.4.1：曲切り替えの修正

前の曲の中継通信を曲切り替え時にキャンセルし、残った接続が次の曲の接続枠を占有する不具合を修正。古いMediaPlayerを閉じる前に参照を無効化し、遅れて届くイベントが現行再生へ干渉しないようにしました。失敗後は再生ボタンで原音の再試行ができ、エラーコードも表示します。変換・音質変更は行いません。

`--switch-test report.json` はテスト環境変数で実サーバーに接続し、連続選曲・キュー置換・シーク・状態通知・失敗からの復帰を音量0で検証します。画像やお気に入り、通常の保存情報は変更しません。




