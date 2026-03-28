# AI向け レビュー結果 T9d WatcherCreatedSnapshot契約復元 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `T9d` は clean worktree で実装役へ渡したうえで、historical 実装と現行コードを照合した
- 結果として、`Created` snapshot queue 契約は現行 snapshot ですでに充足しており、code change 不要と判定した
- 次の実作業レーンは `RenameBridge` 非表示 item 回帰へ進める

## 1. 判定

- 実装判定: `no-op`
- 最終判定: 受け入れ
- commit: 不要

## 2. 確認した対象

- `Watcher/MainWindow.Watcher.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`
- 参考 historical
  - `a085a5d:Watcher/MainWindow.Watcher.cs`

## 3. 充足を確認した契約

- `CreatedWatchEventDirectResult`
- `CreatedWatchEventRuntimeTestHooks`
- `CreatedWatchEventDirectEnqueueRequest`
- `WatchEventRequest` の constructor/signature
- `CaptureWatchEventRequestScope(...)`
- `ResolveWatchEventQueueGuardAction(...)`
- `ProcessWatchEventAsync(WatchEventRequest)` の Created / Renamed dispatch
- `ProcessCreatedWatchEventAsync(WatchEventRequest)`
- `TryEnqueueCreatedWatchEventThumbnailJob(...)`
- `ProcessCreatedWatchEventDirectAsync(...)`
- `ProcessCreatedWatchEventDirectAsyncOverrideForTesting`

## 4. 補足

- clean worktree には `Watcher/MainWindow.WatcherEventQueue.cs` が存在せず、対象契約は `Watcher/MainWindow.Watcher.cs` に集約されていた
- build / test は XAML blocker 前提で未実行
- 次の watcher 実作業は、`RenameBridge` の「表示中 `MovieRecs` 依存へ戻った回帰」を切る
