# Everything Phase2 移植単位一覧（2026-03-03）

## 1. 目的
- `EverythingFolderSyncService` を `EverythingProvider` + `IndexProviderFacade` へ段階移植する単位を固定する。
- 実装時に「どこから手を付けるか」を迷わない状態にする。

## 2. 移植単位（サービス内部）

| No | 現行メソッド/定数 | 移植先 | 扱い |
|---|---|---|---|
| M1 | `SearchLimit` / `ReceiveTimeoutMs` | `EverythingProvider` | そのまま移植 |
| M2 | `GetIntegrationMode` | Facade層 | `Properties.Settings` 依存を排除し、引数 `IntegrationMode` へ置換 |
| M3 | `IsIntegrationConfigured` | Facade層 | `mode != Off` 判定へ置換 |
| M4 | `CanUseEverything` | `EverythingProvider.CheckAvailability` | reason互換を維持して移植（mode非依存） |
| M5 | `TryCollectMoviePaths` | `EverythingProvider.CollectMoviePaths` | 戻り値を `FileIndexMovieResult` へ置換 |
| M6 | `TryCollectThumbnailBodies` | `EverythingProvider.CollectThumbnailBodies` | 戻り値を `FileIndexThumbnailBodyResult` へ置換 |
| M7 | `ExtractThumbnailBody` | `EverythingProvider` 内部ユーティリティ | 共通利用のため private static 維持 |
| M8 | `ParseTargetExtensions` / `BuildEverythingQueries` | `EverythingProvider` 内部ユーティリティ | そのまま移植 |
| M9 | `IsUnderRoot` / `IsDirectChild` / `IsTargetExtension` | `EverythingProvider` 内部ユーティリティ | そのまま移植 |
| M10 | `IsChangedSince` / `TryGetItemChangedUtc` / `NormalizeToUtc` | `EverythingProvider` 内部ユーティリティ | UTC規約を維持 |

## 3. 新規追加（Facade）

| No | 新規要素 | 役割 |
|---|---|---|
| F1 | `IndexProviderFacade` | OFF/AUTO/ON判定、Provider実行、fallback方針決定 |
| F2 | `ScanByProviderResult` | `strategy` / `reason` / `MoviePaths` / `MaxObservedChangedUtc` の返却 |
| F3 | `CollectThumbnailBodiesWithFallback` | サムネBody収集のEverything優先 + fallback統一 |

## 4. 非移植（ホスト残置）
- `LoadEverythingLastSyncUtc` / `SaveEverythingLastSyncUtc`
- `DescribeEverythingDetail`
- `Notification.Wpf` を使う通知表示
- `DebugRuntimeLog` 出力

## 5. 実装順（推奨）
1. `EverythingProvider` の `CheckAvailability` 実装
2. `CollectMoviePaths` 実装
3. `CollectThumbnailBodies` 実装
4. `IndexProviderFacade` 実装
5. 呼び出し側（MainWindow）をFacadeへ差し替え

## 6. 完了条件
- `EverythingFolderSyncService` と同等のreason互換が維持される。
- `MainWindow` は `EverythingSearchClient` へ直接依存しない。
- fallback時の `strategy` / `reason` が条件表どおりに返る。
