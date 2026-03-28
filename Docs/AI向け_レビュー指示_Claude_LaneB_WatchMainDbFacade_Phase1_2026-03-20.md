# AI向け レビュー指示 Claude LaneB WatchMainDbFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- `Lane B: watch movie read/write facade` の Phase1 差分

## 2. 見る観点

- `BuildExistingMovieSnapshotByPath(...)` と `InsertMoviesToMainDbBatchAsync(...)` が facade へ寄っているか
- `watch` の hot path 挙動が変わっていないか
- `visible-only gate` / deferred batch / UI 抑制へ差分が混入していないか
- facade に UI / coordinator / `Dispatcher` 詳細が逆流していないか
- `MovieDbSnapshot` など `Watcher` private 型への依存が将来 DLL 分離と逆向きでないか
- テスト不足が無いか

## 3. finding の出し方

- finding first
- 重大度順
- file:line を付ける

## 4. 受け入れの目安

- `watch` MainDB 2 口が 1 本の facade に見える
- hot path の意味的挙動を変えていない
- `watch` 本体から DB 詳細がさらに隠れている
- `WatcherUiBridge` や `Everything last_sync` へ広がっていない
