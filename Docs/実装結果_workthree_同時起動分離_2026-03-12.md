# 実装結果 workthree 同時起動分離 2026-03-12

## 目的
- `IndigoMovieManager_fork` 本線と `IndigoMovieManager_fork_workthree` を同時起動しても、
  `logs / QueueDb / health 相当の進捗ログ / 集計スクリプト参照先` が混ざらない状態にする。

## 今回の結論
- `workthree` の `LocalAppData` ルートを `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree` に分離した。
- これにより、本線の `%LOCALAPPDATA%\IndigoMovieManager_fork` とは別の保存先を使う。
- まずは保存先衝突の停止を優先し、`AssemblyName / Product / Company` は今回は分離していない。

## 変更内容

### 1. 保存先名を共通定数へ集約
- 追加: `src/IndigoMovieManager.Thumbnail.Engine/AppLocalDataPaths.cs`
- 役割:
  - `RootFolderName = "IndigoMovieManager_fork_workthree"`
  - `LogsPath`
  - `QueueDbPath`

### 2. 本体コードの保存先を新ルートへ変更
- `App.xaml.cs`
  - `firstchance.log`
- `DebugRuntimeLog.cs`
  - `debug-runtime.log`
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbPathResolver.cs`
  - `QueueDb`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - `thumb_decode.log`
- `Thumbnail/Adapters/ThumbnailProgressUiMetricsLogger.cs`
  - `thumbnail-progress-ui.csv`
- `Thumbnail/Adapters/ThumbnailPreviewLatencyTracker.cs`
  - `thumbnail-progress-latency.csv`
- `Thumbnail/ThumbnailCreationService.cs`
  - `thumbnail-create-process.csv`

### 3. テストと手動確認スクリプトを追従
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateProcessCsvFormatTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailEngineBenchTests.cs`
- `Thumbnail/Test/QueueDbPathResolverTests.cs`
- `Thumbnail/Test/TestMockServices.cs`
- `Thumbnail/Test/run_queue_e2e_manual.ps1`
- `Thumbnail/Test/run_thumbnail_engine_bench.ps1`
- `Thumbnail/Test/run_thumbnail_engine_bench_folder.ps1`
- `scripts/export_thumbnail_log_summary.ps1`
- `scripts/export_thumbnail_rescue_summary.ps1`
- `scripts/find_slow_videos.ps1`

## 期待される動作
- `workthree` は以下へ出力する。
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\debug-runtime.log`
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\thumbnail-create-process.csv`
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\QueueDb\*.queue.imm`
- 本線は従来どおり `%LOCALAPPDATA%\IndigoMovieManager_fork\...` を使う。
- そのため、同時起動してもログ・QueueDb・各種集計対象が混ざらない。

## 確認結果
- ビルド成功:
  - `dotnet build IndigoMovieManager_fork.sln -c Debug -p:Platform=x64 -m:1`
- 関連テスト成功:
  - `dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~ThumbnailCreateProcessCsvFormatTests|FullyQualifiedName~ThumbnailEngineBenchTests|FullyQualifiedName~MissingThumbnailRescuePolicyTests|FullyQualifiedName~ThumbnailProgressRuntimeTests"`
  - 結果: `27 passed / 1 skipped / 0 failed`

## 今回あえて触っていないもの
- `ThumbnailLaneClassifier.cs` の `SettingsAssemblyName`
- `.csproj` の `AssemblyName / Product / Company`
- MainDB 形式
- worker 制御ロジックそのもの

## 次の確認項目
1. `workthree` と本線を同時起動し、双方の `debug-runtime.log` が別ファイルに増えることを確認する。
2. それぞれでキュー投入し、`QueueDb` が別フォルダに生成されることを確認する。
3. 集計スクリプトを `workthree` 側で実行し、新保存先のログを読めることを確認する。
