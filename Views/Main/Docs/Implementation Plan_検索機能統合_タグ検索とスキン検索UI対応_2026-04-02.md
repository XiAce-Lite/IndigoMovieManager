# Implementation Plan: 検索機能統合 タグ検索とスキン検索UI対応 2026-04-02

## 1. 目的
- 検索機能を `MainWindow` 内の複数箇所に散らばった状態から整理し、検索仕様の正本を 1 か所へ寄せる。
- 通常の検索 UI と、将来の WhiteBrowser 互換スキン検索 UI が、同じ本体検索を使う構造へ揃える。
- タグを検索の一級対象として扱い、通常検索とタグ専用検索を同じ基盤で解釈できるようにする。
- ユーザー体感テンポを落とさず、既存の `FilteredMovieRecs` 更新導線を守りながら段階移行する。

## 2. 現状の問題

### 2.1 責務が分散している
- `Views/Main/MainWindow.Search.cs`
  - `SearchBox` イベント処理
  - 履歴追加/削除
  - 検索実行の入口
- `Views/Main/MainWindow.xaml.cs`
  - `GetHistoryTable(...)`
  - `FilterAndSort(...)`
  - `SearchCount` 更新
- `ViewModels/MainWindowViewModel.cs`
  - `FilterMovies(...)`
  - `SortMovies(...)`
- `DB/SQLite.cs`
  - `InsertHistoryTable(...)`
  - `InsertFindFactTable(...)`
  - `DeleteHistoryTable(...)`

### 2.2 タグ検索が「通常検索の一部」に埋もれている
- 現状でも `Tags` は通常検索対象に入っている。
- しかし、タグ専用検索の責務が分離されておらず、将来の WB 互換 `!tag` や `tagbar` とつなぎにくい。

### 2.3 スキン検索 UI の受け口がまだ定義し切れていない
- スキンチーム前提は次である。
  - スキンは検索 UI を持てる
  - 検索実行は `wb.*` で本体へ依頼する
  - 本体が `FilteredMovieRecs` を更新する
  - スキンは `onUpdate` / `wb.update()` 相当で再描画する
- つまり検索ロジックはスキンへ持たせず、本体で一元化すべきである。

## 3. 採用方針

### 3.1 検索は「UI は複数、本体は 1 つ」
- WPF の `SearchBox`
- 保存済み検索条件
- WhiteBrowser 互換スキンの検索 UI

これらはすべて別 UI として許容する。
ただし検索判定ロジックと実行本体は 1 つに固定する。

### 3.2 タグは検索の一級市民として扱う
- 通常検索でも `Tags` を検索対象に含める。
- さらにタグ専用検索構文を同じ `SearchService` で扱う。
- これにより、WB 互換の `!tag` や `tagbar` へ自然につながる。

### 3.3 初手では大規模な SQL 化をしない
- 既存の `FilterAndSort(...)` は in-memory の絞り込みを前提に動いている。
- ここをいきなり SQL 主導へ変えると、起動段階ロード化や visible-first 方針とぶつかる。
- 今回の主目的は「責務の統合」であり、「検索エンジンの方式変更」ではない。

## 4. 目標アーキテクチャ

### 4.1 `SearchService`
- 役割
  - 検索文字列の解釈
  - 通常検索
  - タグ専用検索
  - 特殊コマンド検索
- 入力
  - `IEnumerable<MovieRecords>`
  - 検索文字列
- 出力
  - フィルタ済み `MovieRecords[]`
  - 必要なら解析済み検索情報

### 4.2 `SearchExecutionController`
- 役割
  - `SearchKeyword` 更新
  - `FilterAndSort(...)` 呼び出し
  - `SearchCount` 反映
  - 検索実行起点の共通化
- 想定呼び出し元
  - WPF `SearchBox`
  - 保存済み検索条件
  - `wb.find(...)`

### 4.3 `SearchHistoryService`
- 役割
  - 履歴読込
  - 履歴追加
  - 履歴削除
  - `findfact` 更新
- `MainWindow` から `DB/SQLite.cs` 直呼びを減らす。

### 4.4 スキン橋渡し
- スキンは `wb.find(...)` で本体へ検索実行を依頼する。
- 本体が `FilteredMovieRecs` を更新する。
- 更新後にスキンへ `onUpdate` / `wb.update()` 相当を返す。
- スキンは検索結果の描画だけを持つ。

## 5. 検索仕様

### 5.1 通常検索
- 対象フィールド
  - `Movie_Name`
  - `Movie_Path`
  - `Kana`
  - `Kana` から導くローマ字検索文字列
  - `Tags`
  - `Comment1`
  - `Comment2`
  - `Comment3`
- 継続仕様
  - フレーズ検索
  - OR
  - NOT
  - `{notag}`
  - `{dup}`

### 5.2 タグ専用検索
- 初期の正本構文は次とする。
  - `!tag:猫`
  - `!tag:"出演者/日本人"`
  - `!notag`
- 旧 WB 互換としての `!猫` 形式は、後段で追加検討とする。
- 理由
  - 構文が曖昧になりにくい
  - 通常検索との見分けが明確
  - スキン/API からも扱いやすい

### 5.3 タグ比較ルール
- 改行区切り文字列からタグ一覧へ正規化する。
- 比較前に次を行う。
  - 前後空白除去
  - 空タグ除去
  - 大文字小文字非依存
- UI ごとの独自 split は増やさず、`SearchService` 側で統一する。

## 6. WPF とスキンの責務分担

### 6.1 WPF 側
- `SearchBox` は入力とイベントだけを持つ。
- 検索文字列の解釈は持たない。
- `MainWindow.Search.cs` は「検索入口」の partial として維持する。

### 6.2 スキン側
- 検索 UI を持ってよい。
- ただし検索ロジックは持たない。
- `wb.find(...)` で本体へ依頼する。

### 6.3 本体側
- `SearchService` が仕様の正本。
- `SearchExecutionController` が実行の正本。
- `FilteredMovieRecs` と `SearchCount` の更新責務は本体側に残す。

## 7. 実装ステップ

### Phase 1: 検索判定ロジックの切り出し
- `SearchService` 追加
- `MainWindowViewModel.FilterMovies(...)` は薄い委譲へ変更
- テスト追加
  - フレーズ
  - OR
  - NOT
  - `{notag}`
  - `{dup}`
  - 通常検索でタグヒット
  - `!tag:` 構文

### Phase 2: 検索実行入口の統合
- `SearchExecutionController` 追加
- `DoSearchBoxSearch()` を controller 呼び出しへ寄せる
- `SearchBox` の各イベントでバラけている検索実行入口を整理する

### Phase 3: 履歴処理の分離
- `SearchHistoryService` 追加
- `GetHistoryTable(...)` 相当を service 化
- `InsertHistoryTable(...)` / `InsertFindFactTable(...)` / `DeleteHistoryTable(...)` の直呼びを減らす

### Phase 4: スキン検索 UI 接続
- `wb.find(...)` を controller へ接続
- 検索完了後の `onUpdate` / `wb.update()` 相当通知を整理
- スキン独自 UI でも本体検索が使えることを確認する

### Phase 5: 保存済み検索条件との統合
- `SavedSearch` タブを placeholder から実体化する
- 保存済み検索条件も controller を通して実行する
- ここで `tagbar` 相当導線との接続準備を行う

## 8. テスト方針

### 8.1 追加するユニットテスト
- `SearchServiceTests`
  - 通常検索
  - かな検索
  - ローマ字検索
  - タグ検索
  - フレーズ
  - OR / NOT
  - `{notag}`
  - `{dup}`

### 8.2 追加する統合寄りテスト
- `SearchHistoryServiceTests`
  - 履歴重複除去
  - 履歴追加
  - 履歴削除
- 将来
  - `wb.find(...)` 経由で `FilteredMovieRecs` が更新されること

## 9. リスクと回避策

### 9.1 `MainWindow.xaml.cs` を大きく触り過ぎるリスク
- 回避策
  - 初手では `FilterAndSort(...)` の本体は極力維持する
  - 先に service を追加し、呼び出し側だけ薄くする

### 9.2 タグ構文を急に増やして既存検索を壊すリスク
- 回避策
  - 初期の正本は `!tag:` に固定する
  - `!猫` 互換は後段で opt-in 的に追加する

### 9.3 スキン側へ検索ロジックが漏れるリスク
- 回避策
  - `wb.find(...)` は「本体実行依頼」だけにする
  - スキンは結果描画だけに集中させる

## 10. 完了条件
- 検索仕様の正本が `SearchService` へ集約されている
- WPF `SearchBox` とスキン検索 UI が同じ検索結果を返す
- タグ検索が通常検索と同じ基盤で解釈される
- 履歴処理が `MainWindow` 直書きから整理されている
- `FilteredMovieRecs` 更新責務が本体に固定されている
- 検索変更によって体感テンポが悪化していない

## 11. 今回やらないこと
- 全検索の SQL pushdown 化
- WhiteBrowser 完全互換の `!tag` 構文を一気に全部再現すること
- `tagbar` の full 実装
- 保存済み検索条件タブの完成までを同時にやること

## 12. 関連資料
- `Views/Main/MainWindow.Search.cs`
- `Views/Main/MainWindow.xaml.cs`
- `ViewModels/MainWindowViewModel.cs`
- `skin/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `Docs/forAI/WhiteBrowser_タグ仕様書_2026-04-01.md`
- `Views\Main\Docs\かな_ローマ字検索実装.md`
