# Implementation Plan kana導入 既存列保存導線整理 2026-04-01

## 1. 目的

- `movie` / `bookmark` の既存 `kana` 列へ、実際に値が入る状態を作る。
- 既存 `.wb` のスキーマは一切変更せず、空の `kana` だけをアプリ側の保存導線で埋める。
- Windows 標準 API `Windows.Globalization.JapanesePhoneticAnalyzer` を使い、外部通信なしで読みを生成する。

## 2. 現状整理

### 2.1 いま起きていること

- `DB/SQLite.cs` の新規作成スキーマには `movie.kana` / `bookmark.kana` がある。
- `DB/SQLite.cs` の新規作成スキーマには `movie.roma` / `bookmark.roma` もある。
- `Data/MainDbMovieReadFacade.cs` と `Views/Main/MainWindow.xaml.cs` は `kana` を読む前提で実装済み。
- 並び替えも `kana` 前提で入っている。
- しかし `DB/SQLite.cs` の `InsertMovieTable(...)` / `InsertMovieTableBatch(...)` は `kana` を書いていない。
- `DB/SQLite.cs` の `InsertBookmarkTable(...)` も `kana` を書いていない。
- `Watcher/MainWindow.WatcherRenameBridge.cs` も `movie_name` 更新のみで、`kana` は追従していない。

### 2.2 今回の本質

- 問題はスキーマではなく、保存導線が途中で切れていること。
- 新規登録、リネーム、既存データ埋め直しの3経路を揃えないと、`kana` はすぐ空に戻る。

## 3. 前提と判断

### 3.1 採用方針

- `*.wb` は変更しない。
- `ALTER TABLE` を含むスキーマ変更、簡易マイグレ、列追加、index 追加はやらない。
- 今回は既存列 `kana` を正しく使うためのアプリ側修正だけに限定する。

### 3.2 想定外DBの扱い

- もし `kana` / `roma` が欠ける `.wb` が来た場合でも、今回の対応では救済しない。
- 既存のスキーマ検証で安全に中断し、「想定外DB」として扱う。

### 3.3 API 採用上の確認

- 現行 `IndigoMovieManager.csproj` は `net8.0-windows`。
- まず `JapanesePhoneticAnalyzer` がそのまま参照できるかを小さく確認する。
- 参照できない場合だけ、`net8.0-windows10.0.19041.0` への明示化、または Windows SDK 契約参照追加を行う。
- ここは実装前の小スパイクで片付け、かな本体ロジックへ話を広げない。

## 4. 実装方針

### 4.1 かな生成サービスを1か所に寄せる

- 新規に `JapaneseKanaProvider` 相当の薄いサービスを追加する。
- 入力は `movie_name` を基本とし、空なら `movie_path` のファイル名本体を使う。
- `JapanesePhoneticAnalyzer.GetWords(...)` の戻りから `YomiText` を連結する。
- 保存前に `Trim()` し、空文字なら元の名前をフォールバックとして入れる。
- 返却形式は **カタカナ寄せの単純文字列** に固定し、ここで余計な独自辞書は持たない。
- `roma` は既存列だが、今回の主目的は `kana` 充填なのでスコープ外とする。

### 4.2 書き込み経路を3本とも揃える

1. 新規登録
- `DB/SQLite.cs` の `InsertMovieTable(...)` / `InsertMovieTableBatch(...)` で `kana` を INSERT 対象へ追加する。
- `bookmark` 登録も同様に `kana` を保存する。

2. リネーム
- `Watcher/MainWindow.WatcherRenameBridge.cs` で `movie_name` 更新時に `kana` も再計算して更新する。
- `bookmark` 名称追従でも `kana` を再生成する。

3. 既存データ埋め直し
- `kana = ''` の行をバックグラウンドで少量ずつ拾い、段階的に埋める。
- UI 起動や DB 切替の初動を止めないよう、1 バッチ 50 から 200 件程度で区切る。

## 5. 既存データの埋め直し

### 5.1 方針

- 旧DBの全件一括更新はやらない。
- DB オープン完了後に、バックグラウンドタスクで `kana = ''` の行だけ埋める。
- DB 切替やアプリ終了で安全に中断できるよう、短いバッチ単位で進める。

### 5.2 進め方

1. `movie_id` 昇順で `kana = ''` の行を一定件数読む。
2. アプリ側で読みを生成する。
3. まとめて更新する。
4. 残件があれば次バッチへ進む。

### 5.3 進捗保持

- 簡易で済ませるなら `system` テーブルに `kana_backfill_done` だけ持つ。
- 中断再開まで欲しいなら `kana_backfill_last_movie_id` を追加する。
- `system` テーブルへの値追加はスキーマ変更ではないため許容範囲だが、不要なら持たない方がシンプル。
- まずは単純化を優先し、進捗保存なしで毎回「空行だけ少量補完」でもよい。

## 6. 体感テンポを守るための線引き

- `OpenDatafile(...)` 中に全件かな生成は絶対に入れない。
- watch の大量登録では、今の DB batch の流れを壊さず、かな生成だけを前段で差し込む。
- rename bridge では既存の非同期フローに載せ、UI スレッドへ戻さない。
- ログは `main-db-kana-fill` / `main-db-kana-backfill` のように用途を分け、重くしすぎない。

## 7. 変更想定ファイル

- `DB/SQLite.cs`
- `Data/MainDbMovieMutationFacade.cs`
- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Models/MovieInfo.cs` または新規かな生成サービス
- `Views/Main/MainWindow.xaml.cs`
- `BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs`
- `Tests/IndigoMovieManager.Tests/*`

## 8. テスト観点

### 8.1 新規登録

- 新規追加した動画の `kana` が空で入らない。
- batch 登録でも `kana` が入る。

### 8.2 リネーム

- ファイル名変更後、`movie_name` と `kana` が両方追従する。

### 8.3 既存データ埋め直し

- `kana = ''` のみが更新対象になる。
- DB 切替や終了で安全に中断できる。
- 大量件数でも UI 初動が悪化しない。

### 8.4 想定外DB

- `kana` / `roma` 欠落DBは従来通り安全に弾かれる。
- 今回の対応で `.wb` スキーマへ手を入れないことを確認する。

## 9. 実装順

1. `JapanesePhoneticAnalyzer` 利用可否の最小スパイク
2. `kana` 生成サービス追加
3. 新規登録 / rename の書き込み導線追加
4. 既存空データのバックグラウンド埋め直し
5. テスト追加とログ整理

## 10. この計画で避けること

- 独自辞書の先行導入
- `.wb` の列追加や簡易マイグレ
- `.wb` の大規模再構成
- 起動時の全件再計算
- `kana` 対応のためだけの重い index 追加
- かな生成失敗時に登録自体を止めること

## 11. 結論

- 今回は「新規登録時の即時保存」「リネーム追従」「既存空データの後追い補完」の3段で進めるのが最も安全。
- 重要なのは、`kana` 列を増やすことではなく、**既にある `kana` 列へ値が流れ続ける状態を作ること** である。
- 体感テンポ優先の本線方針に合わせ、重い処理は DB オープン後のバックグラウンドへ逃がす。
