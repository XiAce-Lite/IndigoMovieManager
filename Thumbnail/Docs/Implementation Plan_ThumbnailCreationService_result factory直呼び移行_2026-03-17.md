# ThumbnailCreationService result factory直呼び移行

更新日: 2026-03-17

## 目的

`Thumbnail\ThumbnailCreationService.cs` に残した `CreateSuccessResult` / `CreateFailedResult` の wrapper は互換口として維持しつつ、
内部実装側の呼び出しは `ThumbnailCreateResultFactory` へ直接寄せる。

## 今回の反映

- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailCreateWorkflowCoordinator.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailCreateResultFinalizer.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailEngineExecutionCoordinator.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailPrecheckCoordinator.cs`
- `Thumbnail\Engines\FfmpegOnePassThumbnailGenerationEngine.cs`
- `Thumbnail\Engines\FrameDecoderThumbnailGenerationEngine.cs`
- `Thumbnail\Engines\FfmpegAutoGenThumbnailGenerationEngine.cs`

上記では `ThumbnailCreateResultFactory.CreateSuccess(...)` /
`ThumbnailCreateResultFactory.CreateFailed(...)` を直接呼ぶ形へ変更した。

## テスト側

`Tests\IndigoMovieManager_fork.Tests` 配下の実行対象テストも、
wrapper 経由ではなく factory 直呼びへ合わせた。

## 意図

- service 本体の static helper を「互換 API」に縮退させる
- result DTO 構築責務を factory へ固定する
- 今後 wrapper を完全撤去するかどうかの判断をしやすくする

## 次

- `ThumbnailCreationService` に残る wrapper のうち、もう production から参照されないものを棚卸しする
- 互換用途が無い wrapper は削除候補として整理する
