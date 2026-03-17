# Implementation Plan_ThumbnailCreationHostRuntime所属整理_2026-03-17

## 1. 目的

- `DefaultThumbnailCreationHostRuntime` を host 基盤側へ寄せる
- `Engine` は host 契約だけを持ち、app 固有の既定実装から離れる
- app / worker の既定挙動は維持しつつ、外部利用時の依存方向を整理する

## 2. 今回の反映

- `src/IndigoMovieManager.Thumbnail.Runtime/DefaultThumbnailCreationHostRuntime.cs` を追加
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationHostRuntime.cs` は interface と internal fallback のみに整理
- `Thumbnail/ThumbnailCreationService.cs` の既定 ctor は `FallbackThumbnailCreationHostRuntime` を使う

## 3. 境界

- `IThumbnailCreationHostRuntime` は `Engine` 所属の public 契約
- `DefaultThumbnailCreationHostRuntime` は `Runtime` 所属の host 実装
- `FallbackThumbnailCreationHostRuntime` は host 未注入の後方互換だけを担う internal 実装

## 4. 意味

- app / worker は引き続き `DefaultThumbnailCreationHostRuntime` を明示注入する
- `Engine` 単体利用者は host 実装を自由に差し替えられる
- `Engine` 側の既定 host 実装は最小化され、外部 repo 化の障害を減らせる
