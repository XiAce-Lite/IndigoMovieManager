# AI向け 差し戻し指示 Codex T7 LaneBGuard補強 2回目 2026-03-20

最終更新日: 2026-03-20

## 1. 状態

- T7 は 2 回目差し戻し
- 重大度
  - High 1
  - Medium 2

## 2. 修正必須

### F1. High

- `sortId = 28` は startup 側の既定順まで production 変更したが、それを固定する test が無い
- `BuildStartupOrderBySql("28")` 相当の挙動を test で固定すること

### F2. Medium

- movie full reload guard が弱い
- `FilterAndSortAsync` 相当の full reload 経路についても
  - facade 呼び出し存在
  - `GetData(` / `SQLiteConnection` / `SQLiteCommand` など direct DB read 不在
  を他の 3 口と同じ粒度で固定すること

### F3. Medium

- `sortId = 28` の full reload test が、`ORDER BY` 無し結果の `[1,2,3,4]` を固定していて不安定
- 無順序結果そのものを assert しない
- 代わりに
  - source-based で `28` が unknown fallback に束ねられていないことを固定する
  - もしくは別の deterministic な観点で固定する

## 3. 触ってよい範囲

- `Data/MainDbMovieReadFacade.cs`
- `Tests/IndigoMovieManager_fork.Tests/MainDbMovieReadFacadeTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/LaneBFacadeGuardArchitectureTests.cs`

## 4. 完了条件

1. `28` startup 側の production 変更が test で固定されている
2. full reload guard が negative check を持つ
3. 無順序結果へ依存した assert が消えている
4. 対象テスト、build、`git diff --check` を再報告する
