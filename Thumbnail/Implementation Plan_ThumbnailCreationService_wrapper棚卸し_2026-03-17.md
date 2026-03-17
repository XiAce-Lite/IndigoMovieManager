# ThumbnailCreationService wrapper棚卸し

更新日: 2026-03-17

## 目的

`Thumbnail\ThumbnailCreationService.cs` に残った static wrapper のうち、
production から参照されなくなったものを落として service 本体をさらに薄くする。

## 今回の整理

production 側の残参照を helper 直呼びへ移した。

- `Thumbnail\Engines\FrameDecoderThumbnailGenerationEngine.cs`
- `Thumbnail\Engines\FfmpegOnePassThumbnailGenerationEngine.cs`
- `Thumbnail\Engines\FfmpegAutoGenThumbnailGenerationEngine.cs`

その上で `Thumbnail\ThumbnailCreationService.cs` から次を削除した。

- preview frame wrapper
- near-black wrapper
- frame retry wrapper
- auto thumb info wrapper
- image transform wrapper
- image writer wrapper
- shell duration wrapper
- 未使用の `ResolveLayoutProfile(...)`

## 最終整理

最後に残っていた

- `CreateSuccessResult(...)`
- `CreateFailedResult(...)`

も撤去した。

これで `ThumbnailCreationService` から static helper は消え、
公開入口と constructor、DTO 定義だけを持つ pure facade 寄りの形になった。

## 効果

- `ThumbnailCreationService` は公開入口と constructor、DTO 定義寄りの役割へ揃った
- 実装本体は helper / coordinator / engine へ寄った

## 次

- `Thumbnail\Test` 配下の旧資産で `ThumbnailCreationService` に依存している箇所を継続棚卸しする
- facade を保ったまま DTO 定義の置き場をどうするかを判断する
