# Implementation Plan kana導入 既存列保存導線整理 2026-04-01

## 1. 目的

- `movie` / `bookmark` の既存 `kana` / `roma` 列へ、実際に値が入る状態を作る。
- 既存 `.wb` のスキーマは一切変更せず、空の `kana` / `roma` をアプリ側の保存導線で埋める。
- Windows 標準 API `Windows.Globalization.JapanesePhoneticAnalyzer` を使い、外部通信なしで読みを生成する。

## 2. 現状整理

### 2.1 いま起きていること

- 新規作成スキーマには `movie.kana` / `movie.roma` と `bookmark.kana` / `bookmark.roma` がある。
- 検索と並び替えは `kana` 前提で既に実装されている。
- しかし保存導線が不完全だと、新規登録や rename 後に `roma` が空のまま残る。
- 旧DBでも `kana` だけ埋まって `roma` が空、あるいは両方空の行が残り得る。

### 2.2 今回の本質

- 問題はスキーマではなく、保存導線と後追い補完導線が分断されていること。
- 新規登録、リネーム、既存データ埋め直しの3経路を揃えないと `kana` / `roma` はすぐ欠損する。

## 3. 前提と判断

### 3.1 採用方針

- `*.wb` は変更しない。
- `ALTER TABLE` を含むスキーマ変更、簡易マイグレ、列追加、index 追加はやらない。
- 今回は既存列 `kana` / `roma` を正しく使うためのアプリ側修正だけに限定する。

### 3.2 想定外DBの扱い

- 既存のスキーマ検証は維持する。
- ただし保存処理側でも列有無を確認し、存在する列だけを安全に更新する。
- これにより旧DBで列欠落が混じっても、無関係な更新まで巻き込んで失敗しにくくする。

## 4. 実装方針

### 4.1 読み生成サービスを1か所に寄せる

- `JapaneseKanaProvider.GetKana(...)` を `kana` の正本生成に使う。
- `roma` は `JapaneseKanaProvider.GetRomaFromKana(...)` で `kana` から生成する。
- DB保存前に `kana` / `roma` の生成を1か所へ寄せ、insert / rename / backfill で共通利用する。

### 4.2 書き込み経路を3本とも揃える

1. 新規登録
- `DB/SQLite.cs` の `InsertMovieTable(...)` / `InsertMovieTableBatch(...)` で `kana` / `roma` を INSERT 対象へ追加する。
- `InsertBookmarkTable(...)` でも `kana` / `roma` を保存する。

2. リネーム
- `Watcher/MainWindow.WatcherRenameBridge.cs` で `movie_name` 更新時に `kana` / `roma` を再計算して更新する。
- `UpdateBookmarkRename(...)` でも `bookmark` 側の `kana` / `roma` を再生成する。
- `MainDbMovieMutationFacade` に `UpdateRoma(...)` を追加し、単一列更新経路を揃える。

3. 既存データ埋め直し
- `kana = ''`、`roma = ''`、またはその両方の行をバックグラウンドで少量ずつ拾い、段階的に埋める。
- 既存 `kana` がある行はそれを再利用して `roma` だけを埋める。

## 5. 既存データの埋め直し

### 5.1 方針

- 旧DBの全件一括更新はやらない。
- DB オープン完了後に、バックグラウンドタスクで空の `kana` / `roma` だけを埋める。
- DB 切替やアプリ終了で安全に中断できるよう、短いバッチ単位で進める。

### 5.2 進め方

1. `movie_id` 昇順で `kana` / `roma` のどちらかが空の行を一定件数読む。
2. アプリ側で不足分の読みを生成する。
3. まとめて更新する。
4. UI上の `MovieRecords.Kana` / `MovieRecords.Roma` も同時に反映する。
5. 残件があれば次バッチへ進む。

## 6. 体感テンポを守るための線引き

- `OpenDatafile(...)` 中に全件再生成は入れない。
- watch の大量登録では、今の DB batch の流れを壊さず、読み生成だけを前段で差し込む。
- rename bridge では既存の非同期フローに載せ、UI スレッドへ戻さない。
- ログは backfill 単位で短く保ち、初動を悪化させない。

## 7. 変更対象

- `DB/SQLite.cs`
- `Data/MainDbMovieMutationFacade.cs`
- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Views/Main/MainWindow.KanaBackfill.cs`
- `Views/Main/Docs/かな_ローマ字検索実装.md`
- `Tests/IndigoMovieManager.Tests/*`

## 8. テスト観点

### 8.1 新規登録

- 新規追加した動画の `kana` / `roma` が空で入らない。
- batch 登録でも `kana` / `roma` が入る。

### 8.2 リネーム

- ファイル名変更後、`movie_name` / `kana` / `roma` が揃って追従する。
- bookmark rename でも `kana` / `roma` が揃って更新される。

### 8.3 既存データ埋め直し

- `kana` だけ空、`roma` だけ空、両方空のいずれも更新対象になる。
- 既存値がある側は壊さずに不足側だけ埋められる。
- 大量件数でも UI 初動が悪化しない。

## 9. 実装順

1. `kana` / `roma` 生成の共通化
2. 新規登録 / rename の書き込み導線追加
3. 既存空データのバックグラウンド埋め直し
4. テスト追加と doc 更新

## 10. この計画で避けること

- 独自辞書の先行導入
- `.wb` の列追加や簡易マイグレ
- `.wb` の大規模再構成
- 起動時の全件再計算
- 読み列のためだけの重い index 追加
- 読み生成失敗時に登録自体を止めること

## 11. 結論

- 今回は「新規登録時の即時保存」「リネーム追従」「既存空データの後追い補完」の3段で `kana` / `roma` を揃える。
- 重要なのは、新しい列を増やすことではなく、**既にある `kana` / `roma` 列へ値が流れ続ける状態を作ること** である。
- 体感テンポ優先の本線方針に合わせ、重い処理は DB オープン後のバックグラウンドへ逃がす。
