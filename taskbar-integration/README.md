# タスクバー連携試作 0.3.1

2026-11-11。実機はWindows 11 26H1、build 28000.3365、x64。本体は `main-window` の C# / WPF、Explorer内の表示はこのC++ DLLへ分離する。別ウィンドウの重ね描きではなく、タスクバーのXAML GridへAuto列とコントロールを挿入する。

## 実機で確認した範囲

- 検証用Windhawk Portable 1.7.3でDLLをコンパイルし、既存Explorerへ読み込んだ。
- このPCでは `SystemTrayFrameGrid` が取得したXAMLルート内に存在しなかった。`TaskbarFrame/RootGrid` の単一暗黙列を、既存部分のStar列とプレイヤー用Auto列に分けた。未知の構造では挿入しない。
- Explorerから本体へ名前付きパイプで通信し、UIのロードと非ゼロの寸法を確認するフラグを返す。本体で「配置済み」を受信した。
- ユーザーが実際のタスクバーを確認し、表示・他アイコンと非重複・曲名クリックによる本体復帰を報告した。
- ユーザーが曲名の追従、再生／一時停止、前／次の動作を確認した。
- 本体設定からのオン→オフ→オンでUI挿入・除去を確認。同じExplorer PID 34948のまま実行した。
- ×による本体の非表示と、タスクバーの曲名クリックによる復帰を確認。
- 本体の通信自己テスト成功。Unicode、4コマンド、表示オフ時の拒否、未選曲時の制限、表示確認フラグ、無応答クライアント後の復旧、終了処理を検査。

Computer Useはタスクバー自体を操作対象として取得できなかったため、タスクバー上の見え方とボタン入力の結果はユーザーの実操作による確認。本体画面、Windhawk、ログ、コンパイルは開発側で確認した。

## 解除時の不具合と修正

旧試作は解除時にExplorerが2回異常終了した。ローカルのダンプで `0xc0000409`、fast-fail引数10、DLLアンロード後のuser32メッセージ処理経路を確認。タイマーの識別子とDLL内のTIMERPROCを保持する方式に問題があったと判断し、修正した。生ダンプは公開物に含めない。

現在は一意のタイマーIDと、TIMERPROCを指定しないWM_TIMERを使用。解除時にタイマーとウィンドウのサブクラスを除去する。当初の短時間試験では同じExplorer PIDを維持したが、その後の0.3.0終了試験でXAMLからアンロード済みDLL領域へのアクセス違反が発生した。0.3.1では全ClickイベントをUI除去前に登録解除する。長時間安定性の保証ではない。

仕様根拠: [Microsoft KillTimer](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-killtimer)。KillTimerだけではキュー内の既存WM_TIMERは除去されない。

## 手順（開発用）

1. 本体を `dotnet publish -c Release -r win-x64 --self-contained true` でビルド。
2. Windhawkでこの `taskbar-player.wh.cpp` をローカルModとしてコンパイルする。検証済みはWindhawk 1.7.3、x64。MIT由来部分の通知は `THIRD-PARTY-NOTICES.txt`。
3. Modを有効にし、設定の `Show experimental taskbar UI` をオンにして保存する。
4. 本体の設定から「タスクバー内UIを表示（試験版）」をオンにする。
5. 本体の「完全終了」で本体を終了し、Modを無効化するとExplorerから連携を解除する。通常の操作にExplorer再起動は不要。

インストーラー版では表示設定を保存する。UIが配置されたと確認できた場合に限り×で常駐する。連携が切れた場合、隠れていた本体は再表示する。前曲操作は本体と同じく、再生位置が3秒以上の場合はまず曲頭へ戻る。

## IPC

同一ユーザー・同一セッションの `JellyfinMusic.Taskbar.v1.{sessionId}`。本体側は `PipeOptions.CurrentUserOnly`。本体から認証情報、サーバーURL、音声、ファイルパスを渡さない。

要求8 bytes（little endian）：magic `0x314D464A`、コマンド0=ポーリング/1=前/2=再生切替/3=次/4=本体表示。コマンドのbit8はUI配置確認。応答1048 bytes：magic、version=1、visible、playing、canControl、reservedの各uint32、曲名・アーティスト各256 UTF-16コード単位。最大255単位で終端し、サロゲートを途中で切らない。

Explorerのワーカーは約250ms間隔、UIは約300ms間隔。通信はUIスレッド外、各I/Oに700msの制限。本体は要求を1秒で打ち切る。IPC断で表示を除去する。配置に失敗した場合は5秒間隔で再試行する。

## 未完了・対応を宣言しない条件

- 主モニターのみ。異なるDPI、画面解像度変更、狭いタスクバー、複数モニター、自動非表示、縦配置、RTL、他のModとの併用は未検証。
- TrayNotifyWndが別の子ウィンドウの場合は、その幅を専用列内の右余白として確保する。あらゆる構成での非重複を保証しない。
- Explorerの意図的な再起動からの復旧試験、タスクバーの同一プロセス内再生成、OS更新後の互換性は未検証。
- 単体パッケージは `../installer/README.md` を参照。エンドユーザーのWindhawk別導入は不要。自動起動設定は未実装。
- インストーラーはWindhawkエンジンと対応ソースを同梱。アプリはGPL-3.0-only。依存部品の通知は `../installer/THIRD-PARTY-NOTICES.md`。
- Windows内部シンボルとXAML構造を利用する。今回の成功をWindows 11全版への対応とは扱わない。

由来: [Taskbar Fluent Media Player](https://github.com/Salyts/Taskbar-Fluent-Media-Player)、commit `234aaa6f03a8c3ebf9c00ab972fd62046c28dd64`。元の調査記録は `taskbar-feasibility`。
