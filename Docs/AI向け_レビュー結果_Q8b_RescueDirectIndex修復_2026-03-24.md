# Q8b-Direct レビュー結果（RescueDirectIndex 修復）

実施日: 2026-03-24

## 対象差分
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
- `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`
- `Thumbnail/MainWindow.ThumbnailRescueManualPopup.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## チェック結果
- `requestedFailureId` の既存分岐（promote/accepted）に対する追跡不整合を解消し、promote 系でも `TryStart...` 呼び出し時に `requestedFailureId` を渡すようにした。
- `promoted` 時の `manual` スロット追跡が `RememberManualThumbnailRescueSlotRequest` へ流れる経路に戻るため、同一 slot 混線時の表示照合が改善される。
- 主要コンパイルは `dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64` で成功。

## 残件
- テスト実行は同時発生している既存差分が多く、`dotnet test` 全体はベースライン外の差分起因で失敗が残る。
- 失敗の主因:
  - `MainWindow` の分離移行で `TryParseArguments` 署名変更に伴うテスト側の追従不足
  - 旧 API シンボル（`ProcessCreatedWatchEventDirectAsync` など）の再整理差分

## 要確認
- `Q8b-Direct` 本体の reviewer 実装内容としては Findings なし。  
- テスト系は別レーン（`Tests` の未収束差分）で順次回収後、再実行推奨。
