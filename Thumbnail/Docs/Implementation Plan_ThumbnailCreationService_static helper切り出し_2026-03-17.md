# ThumbnailCreationService static helper切り出し

更新日: 2026-03-17

## 目的

`Thumbnail\ThumbnailCreationService.cs` に残っていた static helper の重い実装を外へ寄せる。

- result DTO 構築
- auto thumb 秒配列生成
- preview frame 生成
- frame read retry
- Shell 経由 duration 解決

service 側は互換 wrapper のみを残し、実体は helper へ移す。

## 今回の追加

- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailCreateResultFactory.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailAutoThumbInfoBuilder.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailPreviewFrameFactory.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailFrameReadRetryHelper.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailShellDurationResolver.cs`

## 反映

- `Thumbnail\ThumbnailCreationService.cs`
  - 各 static helper は wrapper 化
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailMovieMetaResolver.cs`
  - Shell duration 解決を helper 直呼びへ変更
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailJobContextBuilder.cs`
  - auto thumb 生成を helper 直呼びへ変更

## テスト

- `Tests\IndigoMovieManager_fork.Tests\ThumbnailPreviewFrameTests.cs`
  - preview frame factory 直呼びへ変更
- `Tests\IndigoMovieManager_fork.Tests\ThumbnailJobContextBuilderTests.cs`
  - auto thumb builder 直呼びへ変更
- `Tests\IndigoMovieManager_fork.Tests\ThumbnailCreateResultFinalizerTests.cs`
  - auto thumb builder 直呼びへ変更
- `Tests\IndigoMovieManager_fork.Tests\ThumbnailAutoThumbInfoBuilderTests.cs`
  - direct helper の規則を追加確認

## 次

- `CreateSuccessResult` / `CreateFailedResult` の呼び出し側も、順次 `ThumbnailCreateResultFactory` へ直接寄せる
- 画像整形系 static helper も `ImageTransform` 系 helper へ分解する
