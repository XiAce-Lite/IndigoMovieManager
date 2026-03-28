# AI向け レビュー指示 Claude LaneB MainDbMovieMutationFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- `Lane B: MainDB single movie mutation facade` の Phase1 差分

## 2. 見る観点

- `UpdateMovieSingleColumn(...)` 直叩きが対象 7 種で facade へ寄っているか
- facade が `columnName` 文字列を UI / watch / thumbnail 側へ漏らしていないか
- `DeleteMovieTable(...)` や `system` / `history` / `bookmark` / `watch` 保存へ差分が広がっていないか
- hot path の意味的挙動が変わっていないか
- facade に UI / `Dispatcher` / coordinator 詳細が逆流していないか
- guard テストと代表的 mutation テストが足りているか

## 3. finding の出し方

- finding first
- 重大度順
- file:line を付ける

## 4. 受け入れの目安

- 対象 7 種の更新が facade 1 本へ見える
- 呼び出し側から SQL 列名文字列が消えている
- 変更範囲が single movie mutation に留まっている
- 代表的更新と guard がテストで固定されている
