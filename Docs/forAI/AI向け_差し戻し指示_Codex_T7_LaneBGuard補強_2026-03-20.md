# AI向け 差し戻し指示 Codex T7 LaneBGuard補強 2026-03-20

最終更新日: 2026-03-20

## 1. 状態

- T7 は review 差し戻し
- 重大度
  - High 1
  - Medium 1
  - Low 1

## 2. 修正必須

### F1. High

- `sortId = 28` を `unknown` と同じ fallback raw order として固定しない
- `28` はアプリ側で `エラー(多い順)` の特別扱いがある
- 今回は
  - `unknown sortId` の既定動作は固定してよい
  - `28` は unknown と束ねず、誤仕様を test に焼かない
- `Data/MainDbMovieReadFacade.cs` の `28` 変更が不要なら戻す

### F2. Medium

- `LaneBFacadeGuardArchitectureTests` の facade 配線 test は存在確認だけで弱い
- facade 呼び出しがあることに加えて
  - `GetSystemTable` / movie full reload / count refresh / startup page 読みで
  - 旧 direct DB read が再混入していないこと
  を negative check で固定する

### F3. Low

- invalid path test の `Z:` 固定をやめる
- GUID 付きの未作成 temp path など、環境非依存の不正パスへ変える

## 3. 触ってよい範囲

- `Data/MainDbMovieReadFacade.cs`
- `Tests/IndigoMovieManager_fork.Tests/MainDbMovieReadFacadeTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatchMainDbFacadeTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/LaneBFacadeGuardArchitectureTests.cs`

## 4. 完了条件

1. `28` と unknown を分離した上で、誤仕様固定が消えている
2. facade 配線 guard が negative check を持つ
3. invalid path test が環境依存でない
4. 対象テスト、build、`git diff --check` を再報告する
