# ThumbnailCreationService internal 生成一本化 実装計画

最終更新日: 2026-03-19

## 目的

- `ThumbnailCreationService.Create(...)` という内部 factory 入口をなくす
- 実装クラスの生成責務を
  - `ThumbnailCreationServiceFactory`
  - `ThumbnailCreationServiceTestFactory`
  の 2 箇所へ一本化する

## 今回の実装

- `Thumbnail/ThumbnailCreationService.cs`
  - internal static `Create(...)` を削除
  - constructor を `internal` に変更
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs`
  - `new ThumbnailCreationService(...)` へ変更
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreationServiceTestFactory.cs`
  - 同様に `new ThumbnailCreationService(...)` へ変更
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreationServiceArchitectureTests.cs`
  - 実装クラスに `Create(...)` が残っていないことをガード追加
  - `new ThumbnailCreationService(...)` が
    - `ThumbnailCreationServiceFactory`
    - `ThumbnailCreationServiceTestFactory`
    にだけ閉じていることをガード追加

## 効果

- 生成責務の所在が 2 箇所へ明確に固定される
- service 本体は facade としての役割だけに寄る
- 内向き factory の二重構造が消え、読み筋がさらに単純になる
