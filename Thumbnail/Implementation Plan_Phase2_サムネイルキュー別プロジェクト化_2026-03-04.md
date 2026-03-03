# Implementation Plan（Phase 2: サムネイルキュー別プロジェクト化 2026-03-04）

## 1. 目的
- キュー処理を `IndigoMovieManager.Thumbnail.Engine` から分離し、責務を「生成」と「運用」に分ける。
- MainWindow から見える挙動（投入、進捗、再試行、自動処理）を維持する。

## 2. スコープ
- IN
  - `Thumbnail/QueueDb/*.cs`
  - `Thumbnail/QueuePipeline/*.cs`
  - `Thumbnail/ThumbnailQueueProcessor.cs`
  - `Thumbnail/ThumbnailProgressRuntime.cs`
  - `src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj`
- OUT
  - `Thumbnail/MainWindow.ThumbnailQueue.cs` のUI制御ロジック変更
  - QueueDBスキーマ仕様変更

## 3. 実装ブロック
- [x] P2-001: `src/IndigoMovieManager.Thumbnail.Queue` プロジェクト追加
- [x] P2-002: Queueプロジェクトへ QueueDb/QueuePipeline/Processor/ProgressRuntime を実体移管（`src` 配下へ配置）
- [x] P2-003: 本体 `IndigoMovieManager_fork.csproj` を Queueプロジェクト参照へ切替
- [x] P2-004: 本体から Queue関連ソースを `Compile Remove` で除外
- [x] P2-005: `.sln` とテストプロジェクト参照へ Queueプロジェクトを追加
- [x] P2-006: ビルド/テスト確認（MSBuild + `dotnet test --no-build`）
- [x] P2-007: 手動回帰（通常キュー、手動再試行、進捗表示）  
  手順書: `Thumbnail/ManualRegressionCheck_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md`  
  進捗: 自動スモーク + ユーザー手動確認を実施済み。
- [x] P2-008: 旧 `Thumbnail` 側の Queue重複ソース削除（完全移管の仕上げ）

## 4. 完了条件（Phase 2 DoD）
- Queueプロジェクトが単体でビルドできる。
- 本体アプリが Queueプロジェクト参照でビルドできる。
- 既存テストが新参照構成で通る。
- 実行時にキュー投入からサムネイル生成まで退行がない。

## 5. 注意点
- `ThumbnailQueueProcessor` は `Notification.Wpf` に依存しているため、現時点では完全なUI非依存ライブラリではない。
- 完全分離は次段で「進捗通知インターフェース化」を行って解消する。

## 6. 検証コマンド
- Queue単体:
  - `dotnet build src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj -c Debug`
- 本体:
  - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- テスト:
  - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --no-build`

## 7. 実行結果メモ（2026-03-04）
- Queue単体ビルド: 成功（0警告 / 0エラー）
- 本体ビルド（MSBuild, Debug|x64）: 成功（警告3 / エラー0）
- テストビルド（MSBuild, Debug|x64）: 成功（警告3 / エラー0）
- テスト実行（`dotnet test --no-build`）: 合格 45 / 失敗 0 / スキップ 2
- Queue自動スモーク（`run_queue_e2e_manual.ps1 -AutoSmokeSeconds 20`）: 成功  
  ログ退避先: `logs/queue-e2e-manual/20260304_053443`
- Queue手動回帰（通常キュー投入 / 再投入 / 進捗タブ更新）: 実施済み（問題なし）

## 8. 移管後の整合チェック（2026-03-04）
- `Thumbnail/*.cs` と `src/IndigoMovieManager.Thumbnail.Queue/**/*.cs` の対象8ファイルを SHA-256 で照合し、全件一致を確認。
- 並列ビルド時に `Thumbnail.Engine` の `obj` DLL ロックが一度発生したため、検証は直列（`/m:1`）で再実行し成功を確認。

## 9. 完全移管クリーンアップ（2026-03-04）
- 削除対象:
  - `Thumbnail/QueueDb/QueueDbPathResolver.cs`
  - `Thumbnail/QueueDb/QueueDbSchema.cs`
  - `Thumbnail/QueueDb/QueueDbService.cs`
  - `Thumbnail/QueuePipeline/QueueRequest.cs`
  - `Thumbnail/QueuePipeline/ThumbnailQueueMetrics.cs`
  - `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs`
  - `Thumbnail/ThumbnailQueueProcessor.cs`
  - `Thumbnail/ThumbnailProgressRuntime.cs`
- 削除後検証:
  - Queue単体ビルド成功
  - 本体ビルド（MSBuild, Debug|x64）成功
  - テストビルド（MSBuild, Debug|x64）成功
  - `dotnet test --no-build` 合格（45/47, skip 2）
