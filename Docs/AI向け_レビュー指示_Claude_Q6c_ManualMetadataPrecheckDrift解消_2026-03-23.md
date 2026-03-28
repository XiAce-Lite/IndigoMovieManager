# AI向け レビュー指示 Claude Q6c ManualMetadataPrecheckDrift解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `ThumbnailCreateWorkflowCoordinatorTests` の修正が、旧 manual metadata precheck 契約へ source を戻さず、現行 thumbnail workflow 契約へ寄ったかを review する

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateWorkflowCoordinatorTests.cs`
- 参照
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateWorkflowCoordinator.cs`
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailJobContextBuilder.cs`

## 3. 必須観点

1. `ThumbnailPrecheckCoordinator` へ manual metadata 即失敗分岐を戻していないか
2. test が `ThumbnailJobContextBuilder` の manual metadata fallback 契約を見ているか
3. rescue 側の「再生成対象へ残す」契約と workflow precheck を混同していないか
4. unrelated change が混ざっていないか

## 4. 受け入れ条件

- findings first
- source 逆流なし
- 変更が最小帯に閉じている
