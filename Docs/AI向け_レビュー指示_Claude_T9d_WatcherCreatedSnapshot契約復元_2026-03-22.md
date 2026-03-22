# AI向け レビュー指示 Claude T9d WatcherCreatedSnapshot契約復元 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `Created` watch queue 要求が DB / tab snapshot を保持して別スコープ汚染を起こさないかを判定する

## 2. レビュー対象

- `Watcher/MainWindow.WatcherEventQueue.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`

## 3. 必ず見る観点

- ready 待機前に request snapshot が確定しているか
- ready 後の runtime 経路が current `MainVM` を引き直していないか
- DB 切替 / tab 切替またぎの stale request をテストで固定できているか
- `RenameBridge` や `WatchScanCoordinator` へ変更が漏れていないか

## 4. 今回レビューしないもの

- `RenameBridge`
- `WatchScanCoordinator`
- `WatchFolderDrop`
- `UI hang`

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
