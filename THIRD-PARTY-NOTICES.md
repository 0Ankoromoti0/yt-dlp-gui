# 同梱ツールについて

v1.1.0のGitHubリリースでは、各OSのFFmpegを `scripts/build-ffmpeg.sh` でソースから静的ビルドします。実際に使ったソース・ビルド手順・ライセンスは、同じリリースの `YtDlpGui-sources-*.tar.gz` に収録しています。yt-dlpとDenoは下記バージョンの公式配布物です。ローカルビルドではPC上のツールをコピーする場合があるため、実際のバージョンは画面下のツール詳細で確認してください。

| ツール | 同梱バージョン | 公式情報 |
| --- | --- | --- |
| yt-dlp | 2026.08.19 | https://github.com/yt-dlp/yt-dlp |
| FFmpeg / ffprobe | 8.0.1（リリース用ソースビルド） | https://ffmpeg.org/ |
| Deno | 2.9.7 | https://github.com/denoland/deno |
| .NET runtime / WPF | 発行時の.NET 10 SDKに対応 | https://github.com/dotnet/runtime / https://github.com/dotnet/wpf |
| Avalonia（mac版） | 11.3.21 / MIT | https://github.com/AvaloniaUI/Avalonia |
| LAME / Opus / libvpx / x264 | 3.100 / 1.5.2 / 1.15.2 / ソースアーカイブの記録参照 | 対応ソースのCOPYING / LICENSE参照 |

yt-dlpのソースはUnlicenseですが、公式PyInstaller版実行ファイルはGPLv3+の依存コードを含みます。
リリースのFFmpegは `--enable-gpl` を指定し、x264・LAME・Opus・libvpxを静的に結合しています。GPLv2以降および各依存物のライセンスに従います。ローカルの旧FFmpegは `--enable-gpl --enable-version3` でした。正確な構成は対応ソースアーカイブの `ffmpeg-build.txt` を参照してください。
DenoはMIT Licenseで、依存物の表記も含めて確認してください。
.NETとWPFのライセンス・第三者表記は実行パッケージに含まれる文書を参照してください。

FFmpegの同梱ライセンスは `licenses/ffmpeg-LICENSE.txt` に保存しています。
yt-dlpの同梱バージョンのUnlicense・第三者表記と実行ファイル向けGPLv3文書、Denoの同梱バージョンのLICENSE、.NET / WPFのライセンス・.NET第三者表記も `licenses` に保存しています。
公式ソースとライセンス文書のリンク:

- yt-dlp: https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE
- yt-dlp依存物: https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt
- FFmpeg: https://ffmpeg.org/legal.html
- Deno: https://github.com/denoland/deno/blob/main/LICENSE.md

アプリ本体のMIT Licenseは同梱のLICENSEを参照してください。

mac版の描画依存物（SkiaSharp / HarfBuzzSharp等）はAvaloniaのNuGet依存関係です。ライセンス表記は `licenses/avalonia-LICENSE.md`、`licenses/skiasharp-LICENSE.txt`、`licenses/skiasharp-THIRD-PARTY-NOTICES.txt` に収録しています。対応ソースは https://github.com/mono/SkiaSharp と https://github.com/AvaloniaUI/Avalonia を参照してください。
