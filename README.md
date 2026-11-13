# Jellyfin Music for Windows

Windows 11向けのJellyfin音楽プレイヤーです。アプリ名は仮称です。

## 構成

- [main-window/](main-window/README.md): WPFのメイン画面、libmpvによる原音再生、Jellyfin接続
- [taskbar-integration/](taskbar-integration/README.md): タスクバー内の曲情報・操作UI
- [installer/](installer/README.md): 単体インストーラー、依存部品のビルド手順とライセンス表示
- [検証記録](installer/VALIDATION.md)

最新版: **0.6.3-preview.1**。アルバム・お気に入り・ライブラリ切り替え、キュー、個別のアルバム／1曲ループ、クリックシーク、再生中のカバー表示に対応しています。変換による再生へのフォールバックは行いません。

## ビルド

.NET 10 SDKで `main-window/MainWindow.csproj` をビルドします。配布物の再作成には [installer/README.md](installer/README.md) を参照してください。GitHub Actionsは `installer/native/` の固定ソースからネイティブ再生部品を生成します。

タスクバー連携にはWindows内部実装を使用しています。確認済みのOSビルドと制約は検証記録を参照してください。

## 保存情報

接続情報は `%LOCALAPPDATA%\JellyfinMusic\connection.bin` にWindowsユーザー単位で暗号化して保存します。パスワードは保存しません。画像キャッシュは同じフォルダーの `artwork/` に保存します。接続情報や認証トークンをリポジトリへ追加しないでください。

## ライセンス・移行元

[GPL-3.0](LICENSE)。依存部品の条件・通知は各THIRD-PARTY-NOTICESを参照してください。

このリポジトリは `palalaw/test` のコミット `6c99a12c0e5efb98f1122590f3f286c11218a531` からアプリのソースを独立させたものです。元の著作権表示・ライセンス・検証記録を保持しています。旧リポジトリの不要ファイルや履歴は含みません。
