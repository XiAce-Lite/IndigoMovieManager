# Everything MainWindow置換ポイント一覧（2026-03-03）

## 1. 目的
- `MainWindow.xaml.cs` / `MainWindow.Watcher.cs` のEverything依存点を、Facade呼び出しへ置換する位置で固定する。

## 2. 置換ポイント（呼び出し箇所）

| No | ファイル | 現行 | 置換後 |
|---|---|---|---|
| R1 | `MainWindow.xaml.cs` | `_everythingFolderSyncService.IsIntegrationConfigured()` | `IndexProviderFacade` の mode判定API |
| R2 | `MainWindow.xaml.cs` | `_everythingFolderSyncService.CanUseEverything(out _)` | `CheckAvailability` |
| R3 | `Watcher/MainWindow.Watcher.cs` | `_everythingFolderSyncService.TryCollectMoviePaths(...)` | `CollectMoviePathsWithFallback(...)` |
| R4 | `Watcher/MainWindow.Watcher.cs` | `_everythingFolderSyncService.TryCollectThumbnailBodies(...)` | `CollectThumbnailBodiesWithFallback(...)` |
| R5 | `Watcher/MainWindow.Watcher.cs` | `_everythingFolderSyncService.IsIntegrationConfigured()` | Facadeまたはmode判定ヘルパー |

## 3. 置換対象外（維持）
- `DescribeEverythingDetail`
- `IsEverythingEligiblePath`（Phase2では維持、後段でProvider側へ集約検討）
- `LoadEverythingLastSyncUtc` / `SaveEverythingLastSyncUtc`
- `RunEverythingWatchPollLoopAsync` / `ResolveEverythingWatchPollDelayMs`

## 4. 差し替え方針
- Step 1:
  - `MainWindow` に `IIndexProviderFacade` フィールドを追加し、既定実装をDIまたはnewで注入する。
- Step 2:
  - `TryCollectMoviePaths` 呼び出し部を `CollectMoviePathsWithFallback` に置換する。
  - `strategy` / `reason` の受け取り型を新DTOへ置換する。
- Step 3:
  - `TryCollectThumbnailBodies` 呼び出し部を `CollectThumbnailBodiesWithFallback` に置換する。
- Step 4:
  - `_everythingFolderSyncService` 直接参照を削除する。

## 5. 注意点
- `strategy` 文字列（`everything` / `filesystem`）は現行互換を維持する。
- フォールバック通知文言は `DescribeEverythingDetail` を継続利用する。
- 置換時にDB処理・UI反映処理は変更しない。

## 6. 完了条件
- `_everythingFolderSyncService` への直接参照がMainWindow系から消える。
- 既存の通知分岐（everything時通知 / fallback時通知）が同じ条件で動作する。
- `CheckMode.Watch` 増分取得で `last_sync_utc` の更新条件が変わらない。
