# 配布 ZIP 同梱 DLL 一覧 v1.0.3 win-x64

最終更新日: 2026-03-29

## 1. この文書の役割

この文書は、配布 ZIP `IndigoMovieManager_fork_workthree-v1.0.3-win-x64` に入っている DLL を役割ごとにざっくり整理した一覧です。

確認元は次の展開先です。

`%USERPROFILE%\Downloads\IndigoMovieManager_fork_workthree-v1.0.3-win-x64\`

## 2. 配布フォルダ直下の DLL

### 2.1 アプリ本体と自作ライブラリ

- `IndigoMovieManager_fork_workthree.dll`
- `IndigoMovieManager.FileIndex.UsnMft.dll`
- `IndigoMovieManager.Thumbnail.Contracts.dll`
- `IndigoMovieManager.Thumbnail.Engine.dll`
- `IndigoMovieManager.Thumbnail.Queue.dll`
- `IndigoMovieManager.Thumbnail.Runtime.dll`

### 2.2 UI 関連

- `AvalonDock.dll`
- `AvalonDock.Themes.VS2013.dll`
- `MaterialDesignColors.dll`
- `MaterialDesignThemes.Wpf.dll`
- `Microsoft.Xaml.Behaviors.dll`
- `Notification.Wpf.dll`
- `VirtualizingWrapPanel.dll`

### 2.3 動画・画像処理関連

- `FFMediaToolkit.dll`
- `FFmpeg.AutoGen.dll`
- `OpenCvSharp.dll`
- `OpenCvSharp.Extensions.dll`
- `OpenCvSharpExtern.dll`
- `opencv_videoio_ffmpeg4110_64.dll`
- `System.Drawing.Common.dll`
- `System.Private.Windows.GdiPlus.dll`

### 2.4 DB・検索・内部基盤関連

- `EverythingSearchClient.dll`
- `System.Data.SQLite.dll`
- `e_sqlite3.dll`
- `SQLitePCLRaw.batteries_v2.dll`
- `SQLitePCLRaw.core.dll`
- `SQLitePCLRaw.provider.e_sqlite3.dll`
- `System.Data.SqlClient.dll`
- `sni.dll`
- `System.IO.Hashing.dll`
- `System.Private.Windows.Core.dll`
- `Microsoft.Win32.SystemEvents.dll`

## 3. tools 配下の DLL

### 3.1 FFmpeg shared DLL

配置先:

`tools\ffmpeg-shared\`

- `avcodec-61.dll`
- `avdevice-61.dll`
- `avfilter-10.dll`
- `avformat-61.dll`
- `avutil-59.dll`
- `swresample-5.dll`
- `swscale-8.dll`

### 3.2 Sinku 関連

配置先:

`tools\sinku\`

- `Sinku.dll`
- `codecs.ini`
- `format.ini`

## 4. DLL 以外で一緒に見ておくとよいもの

- `IndigoMovieManager_fork_workthree.exe`
- `IndigoMovieManager_fork_workthree.deps.json`
- `IndigoMovieManager_fork_workthree.runtimeconfig.json`
- `IndigoMovieManager_fork_workthree.dll.config`
- `README-package.txt`
- `rescue-worker-expected.json`
- `SHA256SUMS.txt`
- `layout.xml`

## 5. 補足

- FFmpeg 系の実 DLL は配布フォルダ直下ではなく `tools\ffmpeg-shared\` にあります
