# AI向け 作業指示 Codex LaneB MainDbMovieMutationFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは実装役である
- 今回は `Lane B: Data 入口集約` の 3 位候補だけを担当する
- 対象は `UpdateMovieSingleColumn(...)` 直叩きの facade 化に限定する

## 2. 目的

- UI と watch rename から MainDB 単一列更新の詳細を隠す
- `MainWindow` / `TagControl` / `Thumbnail` / `Watcher` が column 名文字列を直接握らない形へ寄せる
- 将来 `Data DLL` と `Contracts` へ寄せるための最初の mutation 入口を作る

## 3. 今回の対象

- `DB/SQLite.cs`
- `Views/Main/MainWindow.Tag.cs`
- `Views/Main/MainWindow.Player.cs`
- `Views/Main/MainWindow.MenuActions.cs`
- `UserControls/TagControl.xaml.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Watcher/MainWindow.WatcherRenameBridge.cs`
- 必要なら `Data` 配下の新規 facade
- 関連テスト

今回 facade 化する更新は次に固定する。

1. tag 更新
2. score 更新
3. view_count 更新
4. last_date 更新
5. movie_path 更新
6. movie_name 更新
7. movie_length 更新

## 4. 今回やること

1. MainDB 単一 movie mutation facade を新設する
2. 上記 7 種の更新だけを typed な入口で facade に寄せる
3. 対象呼び出し元の `UpdateMovieSingleColumn(...)` 直叩きを facade 経由へ置き換える
4. テストで facade の guard と代表的な更新を固定する

## 5. 今回やらないこと

- `DeleteMovieTable(...)` の facade 化
- `UpsertSystemTable(...)` の facade 化
- `history` / `bookmark` / `watch` テーブル更新
- watch の `Everything last_sync`
- `FilterAndSort(...)`、`visible-only gate`、deferred batch、UI 抑制の再変更
- `TagEdit` や player の UI フロー変更

## 6. 実装の方向

- 今は実 `Data DLL` project を増やさず、`Data` 配下に仮置き facade を置く
- facade は generic な `columnName` 文字列公開を避け、呼び出し側から SQL 都合を隠す
- 可能なら `IMainDbMovieMutationFacade` と `MainDbMovieMutationFacade` のような中立名を使う
- rename は `movie_path` と `movie_name` を別メソッドでもよいが、呼び出し側に SQL 列名を漏らさないことを優先する
- 既存の `DB/SQLite.cs` 実装は利用してよいが、呼び出し元からは facade 越しに見せる

想定する叩き台:

- `IMainDbMovieMutationFacade`
- `MainDbMovieMutationFacade`
- `UpdateTag(...)`
- `UpdateScore(...)`
- `UpdateViewCount(...)`
- `UpdateLastDate(...)`
- `UpdateMoviePath(...)`
- `UpdateMovieName(...)`
- `UpdateMovieLength(...)`

## 7. 触ってはいけないこと

- `DeleteMovieTable(...)`
- `system` / `history` / `bookmark` / `watch` の保存口
- watch の queue 制御
- `ThumbnailCreationService` 系
- `MainWindow` の大規模責務移動

## 8. 最低限の確認

- facade 対象テストを追加または更新
- 代表的な呼び出し元が facade 経由へ寄ったことを確認
- build
- 対象テスト
- `git diff --check`

## 9. 完了条件

1. 対象 7 種の更新が facade 経由へ寄っている
2. 呼び出し側から `columnName` 文字列が消えている
3. `DeleteMovieTable(...)` や side table 更新へ広がっていない
4. UI / watch / thumbnail の意味的挙動を変えていない

## 10. 次へ渡す相手

- レビュー専任役 Claude / Opus
