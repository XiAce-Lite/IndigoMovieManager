# ThumbnailCreationService image transform helper切り出し

更新日: 2026-03-17

## 目的

`Thumbnail\ThumbnailCreationService.cs` に残っていた画像整形系 static helper を外へ寄せる。

- aspect rect
- default target size
- crop
- resize
- aspect fit rectangle
- aspect ratio

service 側は互換 wrapper のみ残し、実体は helper 側へ移す。

## 今回の追加

- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailImageTransformHelper.cs`

## 反映

- `Thumbnail\ThumbnailCreationService.cs`
  - 上記 6 helper を wrapper 化
- `Thumbnail\Engines\FrameDecoderThumbnailGenerationEngine.cs`
  - default target size / resize を helper 直呼びへ変更
- `Thumbnail\Engines\FfmpegAutoGenThumbnailGenerationEngine.cs`
  - aspect ratio / resize を helper 直呼びへ変更
- `Tests\IndigoMovieManager_fork.Tests\ThumbnailAspectRatioTests.cs`
  - helper 直叩きへ変更
  - default target size / crop の確認を追加

## 効果

- `ThumbnailCreationService` から画像整形本体が抜ける
- decoder / autogen / test で同じ helper を直接使う形に揃う

## 次

- 旧 wrapper 呼び出しが残る箇所を順次 helper 直呼びへ寄せる
- `GetAspectRect` の旧互換経路が不要になった段で削除判断をする
