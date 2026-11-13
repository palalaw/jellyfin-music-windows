# 調査結果

2026-11-06。文献・ソース調査と実機検証を区別する。

## 方式比較

| 方式 | 要件との関係 | 判断 |
|---|---|---|
| ITaskbarList3サムネイルボタン | サムネイル表示時だけ操作可能 | 必須UIの実装として不採用 |
| 通知領域ポップアップ／別ウィンドウ | 常時タスクバー内表示とは異なる | 無断で代替しない |
| AudioBand＋タスクバー置換 | AudioBandのWindows 11手順は外部パッチャーを要求 | 第一候補にしない。シェル変更範囲が広い |
| Windhawk＋XAML列挿入 | 実際のタスクバーのレイアウトへ挿入するコードあり | 第一検証候補。単体配布・安定性は未証明 |
| 独自インジェクター＋小さいDLL | 上記の必要部分のみ同梱する設計案 | 開発・復旧・更新管理の負担が大きく、現段階では未選定 |

Microsoftの公開仕様にはタスクバー領域をアプリがカスタマイズできなくなった旨がある。調査した公開APIでは希望するUIを満たせない。一方、非公開内部構造への介入が一切不可能という意味ではない。

一次資料：

- https://www.microsoft.com/en-us/windows/windows-11-specifications
- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist3-thumbbaraddbuttons
- https://audioband.github.io/AudioBand/audioband/Windows11.html
- https://github.com/Salyts/Taskbar-Fluent-Media-Player
- https://github.com/ramensoftware/windhawk
- https://github.com/ramensoftware/windhawk/wiki/Mod-lifetime

## ソースから確認できたこと

Taskbar Fluent Media Player 1.6.0には、SystemTrayFrameGridにAuto列を挿入する経路がある。重ね描き用の配置経路もあるため、単に同modを採用すれば重なり問題が解決するわけではない。試作品はAuto列の経路だけを使用する。

既存Explorerへの初期化、設定変更、UI除去の処理がある。Windhawkにもロード・設定変更・アンロードのライフサイクルがある。これらは再起動不要方式の候補を裏付けるが、対象ビルド上で成功する証拠ではない。

フック対象はtaskbar.dll内部のCTaskBand、CSecondaryTaskBand、TaskbarHost等。XAMLルートの取得には内部オブジェクト配置の推定が含まれる。Windows更新でシンボル・機械語・XAML階層が変わる可能性を前提にする。未検証ビルドでは連携を無効にして理由を示す設計を検討する。

参照した完全なcommitはevidence.jsonに記録。試作品はソースのスレッド呼び出し、XAMLルート取得、名前探索、シンボル解決を抽出・改変。音楽セッション制御・ビジュアライザー・ポップアップ等は含めない。

## ライセンス判断の現在地

Taskbar Fluent Media PlayerのリポジトリLICENSEはMIT。著作権・許諾表示を同梱する。WindhawkのトップレベルLICENSEはGPLv3。改変・同梱する場合は対応するソース、変更表示、ライセンス文、ビルドに必要な情報の提供等を確認する。

これは全依存部品の監査完了を意味しない。実際に同梱するエンジン、ヘッダー、コンパイラーランタイム、ライブラリの版と条件を固定する必要がある。元リポジトリURLだけを添えることでソース提供義務が常に満たされるとは扱わない。Windhawkを同梱したという理由だけで、分離した音楽アプリに適用されるライセンスまで断定しない。

公開方針はGPL系採用の障害ではない。アプリ全体をGPLv3互換で公開する案を候補にするが、最終ライセンスは未決定。今回の成果物はコンパイラやエンジンのバイナリを配布しない。

- https://github.com/Salyts/Taskbar-Fluent-Media-Player/blob/main/LICENSE
- https://github.com/ramensoftware/windhawk/blob/main/LICENSE

## 条件付き構成案（未採用）

タスクバー連携：C++ DLL＋専用のホスト／管理プロセス。音楽本体は独立したプロセス。DLLに認証情報・HTTP通信・デコード処理を持たせない。タスクバー操作は現在ユーザー限定のIPCで本体へ送り、曲情報と再生状態だけを受け取る。長い処理をExplorerのUIスレッドで実行しない。

本体：C#＋WPF等を比較する。再生エンジンはWindows標準再生系とlibVLC等を、Jellyfinの必要形式・変換・認証付きストリーム・配布サイズ・ライセンスで実測比較して選定する。現時点でフレームワークやエンジンを決めない。

本体が別プロセスでも、DLLの不具合によるExplorer自体への影響は残る。IPC切断・連携プロセス停止・Explorer再起動でも本体が再生を続けられることを別途試験する。

接続プロファイルはserverIdとサーバーURLを独立管理し、認証トークンはユーザー単位の安全な保存先に分離する。公開ソース・ログ・クラッシュレポートにトークンを含めない。初版は1サーバーのみ。

Symfoniumの公式サイトは画面構成の参考として確認した。ブランド、画像、アイコンのコピーは行わない。https://symfonium.app/

## 未確定事項

実機検証用VM／PCへのアクセス方法が次に必要。現在のPCは26H1・28000.3231・x64と観測したが、この版への対応を宣言するものではない。対応Windowsの範囲は検証結果をもとに相談する。

複数モニターに全表示するか特定画面だけか、タスクバー自動非表示に追随するかは製品仕様として未確定。検証ではまず自動非表示に追随する挙動を試し、常時表示と自動非表示の解釈を最終確認する。領域不足時に必須操作を消す仕様は無断で採用しない。
