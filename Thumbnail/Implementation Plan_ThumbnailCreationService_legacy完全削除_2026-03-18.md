# ThumbnailCreationService legacy 完全削除 実装計画

最終更新日: 2026-03-18

## 目的

- obsolete 化済みの互換 constructor / wrapper を repo から完全に除去する
- `ThumbnailCreationService` を `Factory + Args` 本流だけの facade に確定する
- `Legacy` の存在を前提にした build 条件や docs も整理する

## 今回の実装

- `Thumbnail/ThumbnailCreationService.Legacy.cs` を削除した
- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
  から `EnableThumbnailCreationServiceLegacyApi` と条件付き compile を削除した
- `IndigoMovieManager_fork.csproj`
  の `Compile Remove` から `ThumbnailCreationService.Legacy.cs` を外した
- architecture test を更新し、次をガードするようにした
  - public constructor が残っていないこと
  - legacy overload が残っていないこと
  - engine csproj に legacy compile 条件が残っていないこと

## 確認観点

- engine 単体 build が通ること
- root build が通ること
- architecture test と public request 周辺テストが通ること

## 効果

- `ThumbnailCreationService` の正規入口が `ThumbnailCreationServiceFactory` と args 型だけに確定した
- 互換層の寿命管理が不要になり、今後の設計判断が単純になった
- 外だし時に「後方互換のために残した古い入口」が混ざるリスクを消せた
