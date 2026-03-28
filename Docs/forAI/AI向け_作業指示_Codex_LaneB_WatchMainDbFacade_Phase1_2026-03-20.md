# AI向け 作業指示 Codex LaneB WatchMainDbFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは実装役である
- 今回は `Lane B: Data 入口集約` の 2 位候補だけを担当する
- 対象は `watch` の movie read/write 入口の facade 化に限定する

## 2. 目的

- `Watcher/MainWindow.WatcherMainDbWriter.cs` に寄っている MainDB read/write 入口を、後で `Data DLL` へ移しやすい facade に寄せる
- `watch` 本体から `GetData(...)` と `InsertMovieTableBatch(...)` の詳細をさらに隠す
- ただし今回は `watch` の UI bridge や `Everything last_sync` には広げない

## 3. 今回の対象

- `Watcher\MainWindow.WatcherMainDbWriter.cs`
- `Watcher\MainWindow.WatchScanCoordinator.cs`
- `Watcher\MainWindow.Watcher.cs`
- 必要なら `Data` 配下の新規 facade
- 関連テスト

今回 facade 化する入口は次の 2 口に固定する。

1. `BuildExistingMovieSnapshotByPath(...)`
2. `InsertMoviesToMainDbBatchAsync(...)`

## 4. 今回やること

1. `watch` 用 MainDB facade を新設する
2. 上記 2 口を facade 経由へ寄せる
3. `watch` 側は facade が返す DTO / 結果だけを見る形へ寄せる
4. `MovieDbSnapshot` が `Watcher` private 型に閉じているなら、Data 側専用 DTO を作るか、watch 側へ変換境界を置く

## 5. 今回やらないこと

- `WatcherUiBridge` の 1 件再読込
- `Everything last_sync` の read/write
- `watch` テーブルや `system` テーブルの facade 化
- `InsertMovieTableBatch(...)` からの `Sinku` 補完分離
- `watch` の UI 抑制、deferred batch、visible-only gate の再変更

## 6. 実装の方向

- 今は実 `Data DLL` project を増やさず、`Data` 配下に仮置き facade を置く
- facade は `watch` 文脈の read/write 2 口だけに絞る
- `MovieCore` や `MovieDbSnapshot` の扱いは、将来 DLL 分離時に `Watcher` private 型へ依存が残らない形を優先する
- ただし `watch` 側の orchestration や UI 反映は facade に持ち込まない

想定する叩き台:

- `IWatchMainDbFacade`
- `WatchMainDbFacade`
- `LoadExistingMovieSnapshot(...)`
- `InsertMoviesBatch(...)`

## 7. 触ってはいけないこと

- `visible-only gate`
- deferred batch
- UI 抑制
- `WatcherUiBridge` の `Dispatcher` 境界
- `ThumbnailCreationService` 系

## 8. 最低限の確認

- facade の対象テストを追加または更新
- `watch` 関連の既存テストで壊れていないことを最低限確認
- build
- `git diff --check`

## 9. 完了条件

1. `watch` の MainDB read/write 2 口が facade 経由へ寄っている
2. `watch` 本体から DB 詳細がさらに剥がれている
3. `Watcher` private 型への依存を将来 DLL 分離しやすい位置へ押し戻せている
4. `watch` の hot path 振る舞いを変えていない

## 10. 次へ渡す相手

- レビュー専任役 Claude / Opus
