# AI向け レビュー結果 LaneB WatchMainDbFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- T5: LaneB watch movie read/write facade Phase1

## 2. 結論

- `BuildExistingMovieSnapshotByPath(...)` と `InsertMoviesToMainDbBatchAsync(...)` は facade 経由へ寄った
- `watch` 本体から `GetData(...)` と `InsertMovieTableBatch(...)` の直接依存は外れた
- blocking な bug finding は無い
- ただし契約の中立化と guard テスト不足は残る
- T5 は Phase1 として受け入れとする

## 3. finding

### G1. 中

- 内容
  - `IWatchMainDbFacade` と `WatchMainDbMovieSnapshot` が `Data` 層にあるが、契約名と配置が `watch` 文脈へ寄っている
  - `Watcher private` 型依存は消えたが、将来の DLL 分離に向けた契約中立化までは未達
- 影響
  - 次フェーズで `Contracts` / 共有 DTO へ押し出す余地が残る
- 主な確認箇所
  - `Data/WatchMainDbFacade.cs`

### G2. 低

- 内容
  - facade テストは正常系 2 本で、無効パス / 読取失敗 / 空入力ガードの確認が無い
- 影響
  - 呼び出し側を薄くしたぶん、guard 回帰の検知が遅れる
- 主な確認箇所
  - `Tests/IndigoMovieManager_fork.Tests/WatchMainDbFacadeTests.cs`

## 4. 調整役判断

- T5 は受け入れ
- G1 は Phase2 で `watch` 名を外した中立 DTO / 契約へ寄せるタスクとして残す
- G2 は guard テスト補強タスクとして残す

## 5. 次アクション

1. Lane B の 3 位候補である UI 散在 single movie update 入口の facade 化タスクを切る
2. T4 / T5 の residual risk をまとめて guard / 統合テスト補強へ切る
