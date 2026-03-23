# Q8b RescueDirectIndex 修復

## 目的
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs` 〜 `RescueWorkerApplication.cs` の `requestedFailureId` 系の直結経路を整理し、現行の index 修復フローを成立させる。
- 既存の `Q8b L1~L3` の no-op 判定対象を混ぜず、`direct` 経路に限定して差分を最小にする。

## 対象ファイル
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
- `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`
- `Thumbnail/MainWindow.ThumbnailRescueManualPopup.cs`
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## 受け入れ条件
- `requestedFailureId` の参照・引き継ぎが破綻しないこと。
- 失敗時に index 回収と placeholder の cleanup が両立すること。
- 既存テストへの影響を最小化し、必要なら `rescue` 系の再現ケースを 1 本以上固定。

## 追加制約
- `ThumbnailDetailModeRuntime` / `ThumbnailLayoutProfile` / timeout 系 (`DefaultThumbnailNormalLaneTimeoutSec`) は本帯で触らない。
- 既存 dirty の混線を避けるため、`MainWindow.Watcher*` 帯の未確定差分は含めない。

## 成果物
- 実装ファイル変更と、必要なら対応テスト。
- review 済みの差分のみを返却。
