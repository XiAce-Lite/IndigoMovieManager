# AI向け レビュー指示 Claude Q6a RouterExpectationDrift解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- router expectation drift 修正が、source を戻さず test を現仕様へ寄せたかを review する

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
- 参照
  - `Thumbnail/Engines/ThumbnailEngineRouter.cs`
  - `Thumbnail/ThumbnailEnvConfig.cs`

## 3. 必須観点

1. 現仕様 `超巨大動画は autogen 維持` を壊していないか
2. failing test の期待値と test 名が source と docs に整合しているか
3. `ThumbnailEngineRouter` や env default を test 都合で変更していないか
4. 変更が `Q6a` の 1 件に閉じているか

## 4. 受け入れ条件

- findings first
- source より test を直す方針を守っている
- `32GB` 超の既定契約が読める形になっている
