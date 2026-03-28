# AI向け レビュー指示 Claude Q3 WatcherEventQueueコンパイル不整合解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `WatcherEventQueue` partial の compile blocker 解消だけを見て、受け入れ可能か判定する

## 2. レビュー対象

- `Watcher/MainWindow.WatcherEventQueue.cs`
- `Watcher/MainWindow.WatcherRegistration.cs`
- 必要なら対応テスト

## 3. 必ず見る観点

- `QueueWatchEventAsync` / `WatchEventRequest` / `WatchEventKind` が `WatcherRegistration` から見えるか
- event queue 定義が `Watcher` 本体へ責務逆流していないか
- 変更が compile blocker 解消に閉じているか
- `RenameBridge` / `Created snapshot` / `UI hang` の受け入れ済み契約を壊していないか

## 4. 今回レビューしないもの

- `WatchScanCoordinator`
- `RenameBridge` の別帯
- `UI hang`
- docs

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
