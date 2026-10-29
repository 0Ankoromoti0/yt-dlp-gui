# yt-dlp GUI for Windows / macOS

日本語で使える、動画・音声のダウンロードアプリです。Windows 10/11 x64、macOS 14以降（Apple Silicon / Intel）向け。C# / .NET 10を使用し、Windows版はWPF、mac版はAvaloniaで画面を作成しています。画面状態と取得・変換処理は共通です。

## 起動

[GitHub Releases](https://github.com/0Ankoromoti0/yt-dlp-gui/releases) からOSに合うZIPをダウンロードします。

- Windows: `YtDlpGui-win-x64.zip` を展開し、`YtDlpGui` フォルダー内の `YtDlpGui.exe` を起動。
- Apple Silicon Mac: `YtDlpGui-osx-arm64.zip` を展開し、`YtDlpGui.app` をApplicationsへ移して起動。
- Intel Mac: `YtDlpGui-osx-x64.zip` を同じ手順で使用。

mac版はDeveloper ID署名・Apple公証なしで配布します。起動がブロックされた場合は、一度起動を試してから「システム設定 → プライバシーとセキュリティ → このまま開く」で許可してください。`.app` 全体を移動し、内部のファイルだけを移動しないでください。両OSとも.NET、Python、Homebrewの追加インストールは不要です。

以下はWindowsでローカルビルドした場合の配置です。

ビルド済みアプリは `artifacts\YtDlpGui\YtDlpGui.exe` です。実行ファイルをダブルクリックしてください。
同じフォルダーのDLLや `tools` も必要です。別の場所へ移す場合は **YtDlpGuiフォルダー全体** を移してください。.NETやPythonの追加インストールは不要です。

## 使い方

1. 動画または音声のURLを入力し、「情報取得」を押します。URL欄でEnterを押しても取得できます。
2. 「動画」または「音声のみ」を選びます。
3. 動画では保存形式と画質、音声では取得音声・保存形式・必要なら変換ビットレートを選びます。
4. 保存先を指定し、「保存を開始」を押します。
5. 結合・変換も終了すると「保存しました」と表示されます。「保存先を開く」でファイルを確認できます。

保存先と形式設定は次回起動時に復元されます。URL履歴やCookieは保存しません。
設定ファイルは `%LOCALAPPDATA%\YtDlpGui\settings.json` です。
mac版は `~/Library/Application Support/YtDlpGui/settings.json` に保存します。mac版の更新対象yt-dlpは同じ場所の `tools/yt-dlp` にあり、初回起動時に同梱版をコピーします。アプリ本体を読み取り専用の場所へ置いても、エンジンの更新・復元を利用できます。

## 形式と品質

- 動画: MP4（H.264 / AAC互換性優先）、MKV、WebM。画質はURLから取得した実際の候補を表示します。
- MP4で高解像度が出ない場合は、MKVを選んでください。初期版では動画の再エンコードを行いません。
- コーデック情報を取得できない直接URLは、元のMP4/WebM形式を選択できますが「互換性未確認」と表示します。
- 音声: 元の音声を保持、MP3、M4A、Opus、FLAC、WAV。
- MP3 / M4A / Opusは変換ビットレートを選択できます。元データと指定形式が同じ場合も、FFmpegで指定したビットレートへ変換します。
- ビットレートを上げたりFLAC/WAVに変換したりしても、元の音声より音質が上がるわけではありません。
- 音声の自動選択はyt-dlpに任せます。コーデックが異なる候補を単純にビットレートで比較しません。
- 「音声なし」の候補は音声を提供していないURLの場合に表示します。

## 中断・エラー

「キャンセル」で取得と子プロセスを中断できます。アプリを閉じた場合も実行中の処理を中断します。
途中の `.part` や分割データが残る場合があります。再取得で利用できる場合があるため、自動削除しません。
音声変換の途中データは保存先の `.ytdlp-gui` 内に置き、成功時にその処理で作成したデータを削除します。変換失敗時は元データを保持します。
既存のファイルは上書きしない設定です。同じタイトル・IDを別画質で保存する場合は別フォルダーを選択してください。
選択候補が利用できなくなった場合は、別の画質へ自動変更せずエラーにします。「情報取得」を押し直してください。

初期版は単一の動画・音声URLに対応しています。プレイリスト専用URL、ライブ配信、Cookieが必要なログイン取得は対象外です。
動画URLにプレイリスト指定が含まれていても、その動画だけを取得します。

## 取得ツール

`tools` フォルダーに `yt-dlp.exe`、`ffmpeg.exe`、`ffprobe.exe`、`deno.exe` を同梱しています。
mac版では `.app/Contents/MacOS/tools` 内に拡張子なしの同名ツールを同梱します。
ツールはアプリ同梱版を優先し、見つからない場合はPATHも検索します。
画面下の「取得ツール準備完了」を展開すると、バージョンと配置先を確認できます。
「yt-dlpを更新」で同梱エンジンを更新でき、「前のバージョンへ戻す」で更新前の実行ファイルを復元できます。
更新中は取得できません。取得中も更新できません。

## 開発・ビルド

.NET 10 SDKを使用します。SDKがない場合は、作業フォルダー内に準備できます。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-sdk.ps1
```

yt-dlp / FFmpeg / ffprobe / DenoがPATHにあるか、プロジェクト直下の `tools` に配置されている状態で実行します。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

`artifacts\YtDlpGui` に自己完結型アプリを発行し、ローカルHTTPサーバーで実際の取得・変換・結合をテストします。
テスト結果は `artifacts\tests` に保存します。サイト依存のテストではなく、自動生成したテスト素材を使います。

ソース構成:

- `src/YtDlpGui`: WPF画面と画面状態
- `src/YtDlpGui.Mac`: mac版のAvalonia画面とOS操作（画面状態・コマンドはWindows版と共有）
- `src/YtDlpGui.Core`: URL解析、候補選択、取得プロセス、進捗・設定
- `tests/YtDlpGui.Tests`: 外部テストライブラリ不要の検証ハーネス
- `DESIGN.md`: 当初の調査・設計案

mac上のビルドでは.NET 10 SDKをインストールし、拡張子なしの取得ツールを `tools/` に配置して実行します。

```sh
bash scripts/build-mac.sh osx-arm64  # Intel向けは osx-x64
```

GitHub Actionsは各OSのネイティブ環境でFFmpegと必要なコーデックをソースから静的ビルドし、自己完結型アプリを発行します。全OSで統合テストとパッケージの画面起動を確認してからリリースを作成します。`scripts/build-ffmpeg.sh` にビルド手順、リリースの `YtDlpGui-sources-*.tar.gz` に実際に使ったソースを収録します。

画面の確認用PNGを生成する開発オプション:

```powershell
.\artifacts\YtDlpGui\YtDlpGui.exe --render-preview .\artifacts\preview
```

## ライセンス・配布

アプリのコードはMIT Licenseです。同梱ツールはそれぞれのライセンスに従います。
第三者へ配布する場合は `THIRD-PARTY-NOTICES.md` と `licenses` を確認し、採用したツールの対応ソース等の配布条件を満たしてください。
