# AI向け 作業指示 Codex LaneB FacadeGuardIntegrationTests Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは実装役である
- 今回は `Lane B` の Phase1 を壊さないための guard / integration test 補強だけを担当する
- 主眼は test 固定であり、production code の変更は必要最小限に留める

## 2. 目的

- T4 / T5 / T6 で facade 化した入口が、後で UI / watch 側の直叩きへ戻らないようにする
- 残っていた guard 不足を test で埋める
- `sortId = 28` と unknown sortId の既定動作も、この機会に固定する

## 3. 今回の対象

- `Data/MainDbMovieReadFacade.cs`
- `Data/WatchMainDbFacade.cs`
- `Data/MainDbMovieMutationFacade.cs`
- `Tests/IndigoMovieManager_fork.Tests` 配下の関連テスト
- 必要なら source-based architecture test の新規追加

## 4. 今回やること

1. T4 向け
   - `sortId = 28` と unknown sortId の既定動作を明文化し、test で固定する
   - `MainWindow` / startup が movie read facade 経由で配線されていることを固定する
2. T5 向け
   - `LoadExistingMovieSnapshot(...)` の invalid path / read failure guard を test で追加する
   - `InsertMoviesBatchAsync(...)` の null / empty guard を test で追加する
3. T6 向け
   - 単一 movie 更新が UI / watcher 側から `UpdateMovieSingleColumn(...)` 直叩きへ戻らないことを source-based test で固定する

## 5. 今回やらないこと

- T8 の watch 契約中立化
- `Contracts` への移動
- `system` / `history` / `bookmark` / `watch` の facade 化
- `DeleteMovieTable(...)` / `UpsertSystemTable(...)` の整理
- watcher queue 制御や thumbnail queue 制御の変更

## 6. 実装の方向

- 既存の `ThumbnailCreationServiceArchitectureTests` と同じく、必要なら source-based architecture test を使ってよい
- ただし対象は Lane B の facade 配線だけに絞る
- `sortId = 28` と unknown sortId の既定動作は、起動時 page 読みと full reload で説明が割れない形を選ぶ
- production code を触る場合も、Data facade 内だけで閉じることを優先する

## 7. 触ってはいけないこと

- `MainWindow` の大規模責務移動
- `Watcher` の visible-only gate / deferred batch / UI 抑制
- `ThumbnailCreationService` 系
- `WatchMainDbFacade` の契約名変更

## 8. 最低限の確認

- 対象テスト
- build
- `git diff --check`

## 9. 完了条件

1. T4 / T5 / T6 の residual risk が test で減っている
2. Lane B facade 配線の退行を止める test が入っている
3. 変更が Lane B guard / integration test 補強に留まっている

## 10. 次へ渡す相手

- レビュー専任役 Claude / Opus
