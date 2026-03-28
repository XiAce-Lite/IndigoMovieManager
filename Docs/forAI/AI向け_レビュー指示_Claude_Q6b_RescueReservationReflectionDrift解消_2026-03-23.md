# AI向け レビュー指示 Claude Q6b RescueReservationReflectionDrift解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `MissingThumbnailRescuePolicyTests` の修正が、旧 private method 名への依存を外し、現行 watcher throttle 契約へ寄ったかを review する

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`
- 参照
  - `Watcher/MainWindow.Watcher.cs`

## 3. 必須観点

1. `ReleaseMissingThumbnailRescueWindowReservation` を source へ戻していないか
2. test が現行 `TryReserveMissingThumbnailRescueWindow(...)` 契約を見ているか
3. 「時刻ベース throttle」と「予約巻き戻し」を混同していないか
4. unrelated change が混ざっていないか

## 4. 受け入れ条件

- findings first
- source ではなく test を直す方針を守っている
- `MissingThumbnailRescuePolicyTests.cs` 1 ファイルに閉じている
