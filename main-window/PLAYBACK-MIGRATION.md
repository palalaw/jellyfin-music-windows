# 再生基盤の比較・置換（0.6.0-preview.1）

## 判断

従来の WPF MediaPlayer と独自 StreamRelay を削除し、Jellyfin Desktop と同系統の libmpv に通信・バッファリング・デコード・シークを委譲する。Jellyfin の再生情報取得と状態通知には公式 C# SDK 2026.10.12 を使用する。UI、タスクバー IPC、ライブラリ取得・認証・画像キャッシュは維持する。

| 項目 | 従来 | 今回 |
|---|---|---|
| 音声取得・Range・バッファ | 独自HTTP中継＋WPF | libmpvのネットワーク入力 |
| 曲の切り替え | 曲ごとのMediaPlayer生成・中継世代管理 | 単一libmpvのloadfile replace。playlist IDで古い終了イベントを除外 |
| シーク・位置 | WPF位置＋中継 | libmpv absolute+exact / time-pos |
| 再生情報・通知 | 独自API処理 | 公式Jellyfin SDKのPlaybackInfoとSessions API |
| 通知順序 | 再生処理と結合 | MPV Shimを参考にした直列キュー。旧曲Stop→新曲Start |
| キュー・ループ | PlaybackSession | 同クラスで独立して維持 |
| UI | WPF | 維持。バークリック即シーク、ドラッグ中の位置上書きを防止 |

Jellyfin Desktop全体を組み込むとQt/Webフロントエンドも必要となるため、再生エンジンを直接利用する。MPV Shim全体はPythonの遠隔操作クライアントのため組み込まず、状態通知の順序制御を参考にする。上流のソースコードをコピーせず、薄いC ABIアダプターとアプリ固有の調停処理を実装した。公式クライアントの再生スタック全体をそのまま移植したものではない。

## 参照した上流

- [Jellyfin Desktop](https://github.com/jellyfin/jellyfin-desktop): 調査時tree `3fa72e5df57ab52c907b65bb42f2a05b7ae25cd6`。`native/mpvAudioPlayer.js` / `src/player/PlayerComponent.cpp` のloadfile、seek、EOF/STOP区別。
- [Jellyfin MPV Shim](https://github.com/jellyfin/jellyfin-mpv-shim): 調査時tree `e99b9e448196cb73bee8f3b28df5d42ff303fa24`。session_reporterの直列送信。
- [Jellyfin SDK C#](https://github.com/jellyfin/jellyfin-sdk-csharp): 調査時tree `f52eeb4bc846734b785804890ac217279e6ba850`。NuGet `Jellyfin.Sdk` 2026.10.12、MIT。
- [mpv v0.42.0](https://github.com/mpv-player/mpv/releases/tag/v0.42.0): ローカル検証用 `libmpv-v0.42.0-x86_64-w64-mingw32-lgpl.zip`。C client APIで動的ロード。

## 原音限定

PlaybackInfoに EnableDirectPlay=true、EnableDirectStream=false、EnableTranscoding=false、変換プロファイルなしを指定する。Direct Play可のソースのみ採用し、公式SDKが生成するStatic=true URLをlibmpvに渡す。変換や別音質へのフォールバックはしない。非対応・通信障害時は停止して再試行可能にする。OSの共有音声出力までビットパーフェクトを保証するものではない。

認証はHTTPヘッダーで渡し、URLやコマンドラインにトークンを含めない。音源のディスクキャッシュは無効。画像キャッシュとDPAPI接続情報保存は継続。

## 責務

- `IAudioEngine` / `MpvAudioEngine`: libmpvの制御と観測のみ。
- `JellyfinPlaybackService`: SDKによるソース解決・直列通知。ドラッグ中の古い位置通知をまとめる。
- `AudioPlayback`: 最新選曲のキャンセル・世代管理とアプリ状態の接続。
- `PlaybackSession`: キュー、曲／アルバムループ、前／次の方針。エンジンに依存しない。

## 検証結果

Windows実機・許可済みJellyfinサーバーで次を確認（2026-11-11、最終ソースビルド2026-11-12）。接続情報や実際の曲IDはリポジトリに含めない。

- Release publish成功、SelfTest・UI navigation test成功。
- 5曲の切り替え、キュー置換、15回×4組の連続選曲、読み込み失敗後の復旧成功。
- 一時停止位置の安定、停止中／再生中シーク、再開後の進行成功。
- ネイティブEOFからの曲ループ、選曲後のアルバムループ保持成功。
- サーバー側の現在曲、DirectPlay、一時停止・位置・停止通知を照合。通知失敗0。
- Computer Useでアルバム詳細のアーティストのみ表示、バー中央クリックで1:45へ即シーク、一時停止中のつまみドラッグで0:52へ移動を確認（2026-11-12）。再開後に別曲を選択して再生が継続することも確認。タスクバー実装は変更していない。
- NuGet脆弱性情報取得にNU1900（アクセス拒否）が発生。ビルド成功と脆弱性監査完了は区別する。

## 0.6 配布構成（2026-11-13）

開発ブランチで追加された4件のビルド修正を採用。GitHub Actions run 43366607671、commit 4c4900e5ce226197eb08beb4ea257c46f5ff4069 が成功。生成ZIPのSHA256は A8B166ADCFD3E43FE535B80A7C7E8DD37E43D632EA9F8E0831F7874515E72C04 と一致。

配布用libmpv 0.42.0はFFmpeg 8.0.1、libass 0.17.5、FreeType 2.14.1、HarfBuzz 12.2.0、FriBidi 1.0.18、libplacebo 7.387.0と固定したfast_float・Vulkan-Headersからビルド。上流ソースの変更なし。対応ソースZIP全9件、ハッシュ、ビルド・梱包スクリプト、ライセンス原文と著作権表示を同梱。詳細はinstaller/nativeとインストール先licenses/PLAYBACK-NOTICES.mdを参照。

スクリプト機能なしのビルドではload-scripts/ytdlオプション自体が存在しないため、本体はこの2項目についてのみOPTION_NOT_FOUNDを許容する。対応機能があるビルドでは従来通り無効化を要求し、その他の初期化エラーは失敗として扱う。

この配布用DLLで実サーバーの連続選曲・シーク・一時停止／再開・EOF曲ループ・アルバムループ保持・サーバー現在曲／DirectPlay／位置／停止通知の検証がすべて成功。通知失敗0。UI回帰テストと自己テストもexit code 0。0.5.2の配布済みインストーラーにはこれらの変更は含まれない。

