# ThumbnailCreationService DTO切り出し

更新日: 2026-03-17

## 目的

`Thumbnail\ThumbnailCreationService.cs` に残っていた DTO 定義を外へ出し、
facade 本体と型定義の責務を分ける。

対象:

- `ThumbnailCreateResult`
- `ThumbnailPreviewFrame`
- `ThumbnailPreviewPixelFormat`

## 今回の反映

- 追加
  - `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailCreateTypes.cs`
- 更新
  - `Thumbnail\ThumbnailCreationService.cs`

## ねらい

- `ThumbnailCreationService` を公開入口と依存組み立てへ寄せる
- DTO は engine 側の中立型として単独参照しやすくする
- 今後 `ThumbnailCreationService` を pure facade として扱いやすくする

## 次

- `ThumbnailCreationService.cs` の using と field を最小化する
- facade 生成責務と host/runtime 生成責務の分離を検討する
