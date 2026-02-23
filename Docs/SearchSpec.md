# 検索仕様（現行実装）

## 1. 目的
- 本書は `../MainWindow.xaml.cs` の現行検索挙動を固定し、改修時の回帰を防ぐための仕様メモ。
- 対象は「一覧絞り込み」「検索履歴」「検索語統計（findfact）」。

## 2. 対象範囲
- 検索入力UI: `../MainWindow.xaml` の `SearchBox`（`ComboBox`）
- 絞り込み本体: `../MainWindow.xaml.cs` の `FilterAndSort`
- 履歴操作: `../MainWindow.xaml.cs` の `GetHistoryTable` / `SearchBox_PreviewKeyDown`
- DB更新: `../DB/SQLite.cs` の `InsertHistoryTable` / `DeleteHistoryTable` / `InsertFindFactTable`

## 3. 検索対象フィールド
- `Movie_Name`
- `Movie_Path`
- `Tags`
- `Comment1`
- `Comment2`
- `Comment3`

比較は `CurrentCultureIgnoreCase` の部分一致（`Contains`）で行う。

## 4. 入力ルール
### 4.1 空文字
- 検索文字列が空の場合は全件表示。

### 4.2 クォート検索
- 先頭末尾が `"` または `'` の場合、外側を除去した文字列で検索。
- 実装は完全一致ではなく「部分一致」。

### 4.3 特殊コマンド
- `{notag}`: `Tags` が空文字のレコードを抽出。
- `{dup}`: `Hash` が重複しているレコードを抽出（空Hashは除外）。

### 4.4 通常検索（AND/OR/NOT）
- OR: `半角スペース + | + 半角スペース`（`" | "`）でグループ分割。
- AND: 各ORグループ内を半角スペースで分割し、全語一致で成立。
- NOT: 語頭が `-` の語は除外条件として扱う。
- 例:
  - `cat dog` -> `cat` AND `dog`
  - `cat | dog` -> `cat` OR `dog`
  - `cat -dog` -> `cat` AND NOT `dog`

## 5. 検索実行タイミング
- DBオープン時: 履歴読込後に `FilterAndSort(..., true)` を実行。
- 履歴ドロップダウン選択時: `DropDownClosed` でユーザー選択フラグがある場合に検索実行。
- テキスト変更時: 現在は「空文字になった時のみ」検索実行（インクリメンタル検索はコメントアウト）。
- Enterキー: 検索結果件数が 1 件以上の時のみ履歴追加（検索実行自体はここでは行わない）。
- IME入力中: `_imeFlag` により検索キー処理を抑制。

## 6. 履歴仕様
### 6.1 表示
- `history` テーブルから `find_text` ごとの最新1件を表示（新しい順）。

### 6.2 追加
- `find_text` が既存なら追加しない（同一文字列は1件運用）。
- `find_id` は `max(find_id)+1` で採番。

### 6.3 削除
- ドロップダウン選択中に Delete キーで、UIから即時削除後にDB削除。

### 6.4 保持件数
- アプリ終了時に `keepHistory`（`system` テーブル）件数へトリム。
- 未設定時は `30` を使用。

## 7. findfact（検索語統計）仕様
- `SearchBox` がフォーカスを失った時に更新。
- 既存語は `find_count + 1`、未登録語は新規作成。
- `last_date` を更新。

## 8. 既知制約・差分候補
- コメント上は「完全一致」とあるが、実装は部分一致。
- OR区切りは `" | "` 固定（`a|b` はOR扱いにならない）。
- Enterキー単独での検索確定動作は明示実装されていない。
- TODOコメントと現実装（NOT/OR一部対応）の整合が取れていない。

## 9. 参照コード
- `../MainWindow.xaml.cs:866`
- `../MainWindow.xaml.cs:950`
- `../MainWindow.xaml.cs:2367`
- `../MainWindow.xaml.cs:2469`
- `../MainWindow.xaml.cs:2486`
- `../MainWindow.xaml.cs:2530`
- `../MainWindow.xaml.cs:550`
- `../MainWindow.xaml.cs:267`
- `../DB/SQLite.cs:568`
- `../DB/SQLite.cs:645`
- `../DB/SQLite.cs:677`
