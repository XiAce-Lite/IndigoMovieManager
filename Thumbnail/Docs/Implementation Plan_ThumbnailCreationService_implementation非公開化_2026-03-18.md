# ThumbnailCreationService implementation 非公開化 実装計画

最終更新日: 2026-03-18

## 目的

- `ThumbnailCreationService` 実装クラスを public 面から外す
- 呼び出し側の正式契約を `IThumbnailCreationService` に固定する
- `Factory + Args + Interface` をコード上の唯一の公開入口にする

## 今回の実装

- `Thumbnail/ThumbnailCreationService.cs` を `internal sealed` に変更した
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreationServiceArchitectureTests.cs`
  で implementation class が non-public であることをガードした

## 効果

- production から concrete class を直接参照できなくなった
- 実装差し替えや内部整理を、呼び出し側へ漏らさず進めやすくなった
- `ThumbnailCreationServiceFactory` が唯一の公開生成口、`IThumbnailCreationService` が唯一の公開契約、という整理が compiler 上も成立した
