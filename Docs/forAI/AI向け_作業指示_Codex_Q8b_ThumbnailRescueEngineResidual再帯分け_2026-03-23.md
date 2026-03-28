# AI向け 作業指示 Codex Q8b ThumbnailRescueEngineResidual再帯分け 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- thumbnail rescue / engine / worker に残る dirty を、実装修正帯と test/docs drift 帯へ再分割する。

## 2. 主対象

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
- `Thumbnail/MainWindow.ThumbnailRescueManualPopup.cs`
- `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/*`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
- 関連 test
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailFailurePlaceholderWriterTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailJpegMetadataWriterTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailRescueWorkerLauncherTests.cs`

## 3. やること

1. dirty を rescue UI / worker launch / engine policy / compatibility / failure db に分ける
2. 既に本線取り込み済みの `Q6c/Q6d` と衝突しない帯だけを抽出する
3. commit 単位として戻せる最小帯を列挙する

## 4. 返却物

- 帯一覧
- 各帯の対象ファイル
- 衝突リスク
- 次に切るべき最小レーン
