# ThumbnailCreationService legacy API分離 実装計画

最終更新日: 2026-03-18

## 目的

- `ThumbnailCreationService` の canonical 本体から obsolete 化済み API を物理的に分離する
- 互換レイヤーを残しつつ、本体ファイルでは正規入口だけを読める状態にする
- 将来の完全削除時に、削除対象を `Legacy` ファイルへ閉じ込める

## 今回の実装

- `Thumbnail/ThumbnailCreationService.cs` を partial 化した
- canonical 本体には次だけを残した
  - private field
  - internal create
  - private ctor
  - `CreateBookmarkThumbAsync(ThumbnailBookmarkArgs, CancellationToken)`
  - `CreateThumbAsync(ThumbnailCreateArgs, CancellationToken)`
- obsolete 化済みの constructor / wrapper は `Thumbnail/ThumbnailCreationService.Legacy.cs` へ移動した

## 効果

- service の本体ファイルを見るだけで、正規入口がどれか分かる
- 互換 API は維持したまま、削除準備の境界を明確にできる
- `Factory + Args` を本流、`Legacy` を後方互換として構造上も分離できる

## 2026-03-18 追記

- `ThumbnailCreationService.Legacy.cs` の obsolete API には `EditorBrowsable(EditorBrowsableState.Never)` も付与した
- これで IDE 候補でも正規入口より legacy API が前面に出にくくなった
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreationServiceArchitectureTests.cs` を追加し、次をガードした
  - public constructor / legacy wrapper に `Obsolete + EditorBrowsable(Never)` が維持されること
  - `ThumbnailCreationServiceFactory` の public 面が正規入口 3 本に留まること
  - service 外からの `new ThumbnailCreationService(...)` 再流入がないこと
  - `CreateForTesting(...)` の利用がテスト領域に閉じていること
- その後 repo 内の旧入口参照がゼロになったため、この段階の役目は終わった
- 完全削除後の到達点は `Implementation Plan_ThumbnailCreationService_legacy完全削除_2026-03-18.md` に移した
