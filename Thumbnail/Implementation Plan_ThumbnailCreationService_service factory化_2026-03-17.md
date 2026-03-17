# ThumbnailCreationService service factory化 実装計画

最終更新日: 2026-03-17

## 目的

- testing 用生成入口を service 本体から外し、`ThumbnailCreationService` を pure facade に近づける
- service 生成に関する名前付き入口を `ThumbnailCreationServiceFactory` へ集約する

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs` を追加
- `CreateDefault()`
- `Create(...)`
- `CreateForTesting(...)`
  を factory 側へ集約
- `ThumbnailCreationService` には内部の最小生成口 `Create(ThumbnailCreationOptions)` だけを残した
- テストと legacy test の engine 差し替え呼び出しは `ThumbnailCreationServiceFactory.CreateForTesting(...)` へ移行

## 効果

- service 本体から test 専用の意味を持つ static helper が消える
- constructor / public method / 最小生成口だけに役割が絞られる
- factory 名で用途が見えるので、将来 DI や composition root へ寄せる時の足場になる

## 2026-03-18 追記

- `ThumbnailCreationService` の public constructor から `ThumbnailCreationOptions` / `ThumbnailCreationServiceComponentFactory` への直接依存も外した
- service は `ThumbnailCreationServiceFactory` が返す composition だけを受け取る形へ変更した
- これで service 本体に残る責務は public API と composition 受け取りだけになった
