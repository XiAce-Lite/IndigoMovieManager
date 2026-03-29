# Implementation Plan_救済worker_PublishArtifact自立化_2026-03-17

## 1. 目的

- `IndigoMovieManager.Thumbnail.RescueWorker` を、`Launcher` の overlay 補完に依存しない publish artifact として作れるようにする
- `Factory` が同一 repo の `bin` よりも publish artifact を優先して選べるようにする
- `OverlaySupplementalDependencies(...)` は publish artifact 選択時に空で済む状態を固定する

## 2. 今回の反映

- `Publish-RescueWorkerArtifact.ps1`
  - `dotnet publish` を実行し、`artifacts/rescue-worker/publish/<Configuration>-<Runtime>` を生成
  - worker 単体では不足する `Images/noFile*.jpg` と `tools/ffmpeg-shared` を publish 出力へ同梱
  - `e_sqlite3.dll` と `SQLitePCLRaw*.dll` / `System.Data.SQLite.dll` も publish/build 出力から完成形へ揃え、FailureDb 初期化で落ちないようにした
  - `tools/ffmpeg/LICENSE-ffmpeg-lgpl.txt` と `ffmpeg.exe` がある環境ではそれも同梱
  - 完成したフォルダへ `rescue-worker-artifact.json` を書き、`compatibilityVersion` も含めて Factory が完成済み artifact と判定できるようにした
- `ThumbnailRescueWorkerLaunchSettingsFactory`
  - 環境変数 override の次に、repo 配下 `artifacts/rescue-worker/publish/Release-win-x64` と `Debug-win-x64` を探索
  - marker 付き artifact でも `compatibilityVersion` が一致しないものは採用しない
  - marker があっても SQLite 関連 DLL や `e_sqlite3.dll` が欠ける不完全 artifact は採用しない
  - 互換な artifact を採用した時は `SupplementalDirectoryPaths` / `SupplementalFilePaths` を空で返す
- `ThumbnailRescueWorkerLauncher`
  - 起動時ログに `origin / worker path / generation / overlay count` を出し、artifact と build の取り違えを runtime log だけで追えるようにした

## 3. 今の意味

- app host は `Factory` を差し替えずに publish artifact 優先へ移れた
- launcher は引き続き session copy を維持するが、publish artifact 経路では overlay が不要になった
- 開発中の `bin` フォールバックはまだ残すため、publish 未作成でも従来どおりローカル起動できる

## 4. 運用手順

PowerShell 7 で次を実行する。

```powershell
./src/IndigoMovieManager.Thumbnail.RescueWorker/Publish-RescueWorkerArtifact.ps1 `
  -Configuration Release `
  -Runtime win-x64
```

生成物:

- `artifacts/rescue-worker/publish/Release-win-x64/IndigoMovieManager.Thumbnail.RescueWorker.exe`
- `artifacts/rescue-worker/publish/Release-win-x64/rescue-worker-artifact.json`

## 5. 残件

- CI での自動生成は追加済み。次は app release との連携方法を詰める
- 本体バージョンと worker バージョンの厳密対応付けまではまだ未実装
- `OverlaySupplementalDependencies(...)` 自体は、開発用 `bin` フォールバックを残すためまだ削除しない
