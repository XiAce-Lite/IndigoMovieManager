# AI向け 作業メモ LaneB Phase1残課題 2026-03-20

最終更新日: 2026-03-20

## 1. 目的

- Lane B の Phase1 受け入れ後に残った補強項目を、次サイクル用に固定する
- 実装役が T4 / T5 / T6 の残課題を混ぜずに切り出せる粒度へ落とす

## 2. 次サイクル候補

### T7: LaneB facade guard / integration tests

- 対象
  - `Data/MainDbMovieReadFacade.cs`
  - `Data/WatchMainDbFacade.cs`
  - `Data/MainDbMovieMutationFacade.cs`
  - 関連テスト
- 目的
  - facade 化した入口が呼び出し側から維持されることを test で固定する
- 最低限の論点
  - T4 の `sortId = 28` と unknown sortId の既定動作
  - T4 の `MainWindow` / startup 配線が facade 経由であること
  - T5 の invalid path / read failure / empty input guard
  - T6 の caller 側から SQL 列名文字列が戻らないこと

### T8: LaneB watch 契約中立化 Phase2

- 対象
  - `Data/WatchMainDbFacade.cs`
  - 必要なら shared contract 置き場
  - 関連テスト
- 目的
  - `IWatchMainDbFacade` と `WatchMainDbMovieSnapshot` の `watch` 文脈依存を薄める
- 最低限の論点
  - `watch` 名の外し方
  - DTO の中立配置
  - `Data` と `Contracts` の次の切り分け

## 3. 進め方

1. 先に T7 を切る
2. T7 受け入れ後に T8 を切る
3. T8 は rename だけで終わらせず、将来 DLL 分離へ向く契約名まで揃える
