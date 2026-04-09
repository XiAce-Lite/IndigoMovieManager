# タグインデックスキャッシュ実装計画

更新日: 2026-04-09

変更概要:
- Phase 1 として `TagIndexCacheService` のメモリ cache を実装した
- 右タブは固定タグに加えて `movie.tag` 集計由来のタグ一覧を表示する形へ拡張した
- 右タブの一覧はスクロール可能にし、初回は固定タグを先に出し、cache 構築完了後に差し替える挙動へした
- タグ更新時は既存の主要 UI 導線から cache へ差分反映するようにした

## 0. 実装進捗

### 0.1 Phase 1 完了

- `BottomTabs/TagEditor/TagIndexCacheService.cs`
  - `movie.tag` をバックグラウンドで集計し、`tag -> 件数` と `movieId -> tags[]` を保持する
- `BottomTabs/TagEditor/MainWindow.BottomTab.TagEditor.cs`
  - 右タブは固定タグの即時表示を維持しつつ、snapshot 完成後は DB 由来タグ一覧を追加表示する
- `BottomTabs/TagEditor/TagEditorTabView.xaml`
  - 右タブのタグ一覧をスクロール可能にした
- 主要なタグ更新導線
  - `TagEditor`
  - `MainWindow.Tag`
  - `UserControls.TagControl`
  から cache へ差分同期する

### 0.2 まだやっていないこと

- sidecar DB 永続化
- 件数順 / 名前順の切替 UI
- 入力補完 UI
- 削除や外部更新まで含めた全経路の invalidation 自動化

## 1. 背景

下部タブ `タグ` の右側へ「全タグ」を出したくなった時、毎回 `movie.tag` を全件走査して改行分解・集計すると、DBサイズ増加に比例して体感が悪化する。

このプロジェクトでは WhiteBrowser 互換を保つため、`*.wb` のスキーマ変更は行わない。

そのため、候補は次の 2 つになる。

- メモリ上にタグ index cache を持つ
- `*.wb` とは別の sidecar DB にタグ index を持つ

## 2. 結論

本命は **`*.wb` 変更なしの二段構え** とする。

1. first step は `TagIndexCacheService` のメモリ cache
2. 必要になったら sidecar DB 永続化を追加

この順にする理由は次の通り。

- `*.wb` 互換を壊さない
- まずは実装が軽い
- UI の体感改善をすぐ得られる
- 将来 sidecar へ拡張しても責務をそのまま引き継げる

## 3. 比較

### 3.1 `*.wb` にタグテーブル追加

利点:

- 検索・集計・件数取得が最も素直
- SQL でタグ一覧を直接扱いやすい

欠点:

- WhiteBrowser 互換の前提と衝突する
- DB配布・移行・障害調査の責任が増える
- 今回の方針では採用不可

結論:

- 不採用

### 3.2 メモリ cache

利点:

- `*.wb` を触らない
- first step として実装しやすい
- UI 表示はかなり軽くできる

欠点:

- 起動ごとに再構築が必要
- 大規模DBでは初回集計が重い
- 再利用性は sidecar より弱い

結論:

- first step として採用

### 3.3 sidecar DB

利点:

- `*.wb` を触らない
- 再起動後も index を再利用できる
- 全タグ表示、件数表示、将来のタグ検索高速化に効く

欠点:

- 破損時再構築、整合性、DB切替の責務が増える
- first step としては少し大きい

結論:

- second step で採用候補

## 4. 目標

### 4.1 直近目標

- 右タブで「固定タグ」だけでなく「よく使うタグ」「全タグ」へ広げられる土台を作る
- タグ候補表示を UI から分離する
- 表示時の全件走査を避ける

### 4.2 中期目標

- sidecar DB により、再起動後もタグ index を再利用する
- タグ件数表示やタグ補完を安定して高速化する

## 5. 設計方針

### 5.1 正本データ

タグの正本は引き続き `movie.tag` の改行区切り文字列とする。

- `record.Tag`
- `record.Tags`
- `movie.tag`

ここは既存の保存導線を維持し、正規化済みの表示/検索用 index は別で持つ。

### 5.2 cache の責務

`TagIndexCacheService` は「表示・候補・件数」のための index を持つ。

責務:

- 全タグ一覧を返す
- タグごとの件数を返す
- よく使うタグの並びを返す
- 動画1件のタグ差分から index を更新する
- DB切替時に cache を切り替える

責務に含めない:

- `movie.tag` そのものの保存
- UI 描画
- 検索本体の仕様解釈

### 5.3 sidecar の責務

`TagIndexStore` は永続化だけを担う。

責務:

- DB単位のタグ index 読込
- DB単位のタグ index 保存
- バージョン不一致時の無効化
- 再構築要求の受付

## 6. データ構造案

### 6.1 メモリ cache

```csharp
internal sealed class TagIndexSnapshot
{
    public string DbFullPath { get; init; } = "";
    public DateTime BuiltAtUtc { get; init; }
    public IReadOnlyDictionary<string, int> TagCounts { get; init; }
    public IReadOnlyDictionary<long, string[]> MovieTags { get; init; }
}
```

要点:

- `TagCounts`
  - 右タブ表示
  - 件数ソート
  - 人気タグ抽出
- `MovieTags`
  - 差分更新用
  - 動画1件の更新時に旧タグ/新タグを比較

### 6.2 sidecar DB

sidecar 側は `*.wb` とは別ファイルとする。

例:

- `%LOCALAPPDATA%\\IndigoMovieManager\\tag-index\\...`

候補テーブル:

```sql
create table tag_index (
    db_identity text not null,
    tag_name text not null,
    use_count integer not null,
    updated_utc text not null,
    primary key (db_identity, tag_name)
);

create table tag_index_meta (
    db_identity text primary key,
    schema_version integer not null,
    source_last_write_utc text not null,
    source_movie_count integer not null,
    updated_utc text not null
);
```

補足:

- `db_identity` は `DBFullPath` をそのまま使わず、必要ならハッシュ化する
- sidecar は `*.wb` の拡張ではなく、完全に別管理とする

## 7. 更新タイミング

### 7.1 初回構築

- DB open 後
- UI 初回表示では固定タグだけ先に出す
- バックグラウンドで tag index を構築
- 完成後に右タブ表示を更新

### 7.2 差分更新

タグ追加/削除時:

1. 既存どおり `movie.tag` を更新
2. 変更前のタグ配列と変更後のタグ配列を比較
3. `TagIndexCacheService` の件数を増減
4. 必要なら sidecar も遅延保存

### 7.3 再構築

次の条件では再構築する。

- DB切替
- sidecar バージョン不一致
- sidecar 破損
- `movie` 件数や最終更新時刻の整合が崩れた時
- 手動リロード要求

## 8. UI での使い方

### 8.1 右タブ

右タブは `TagIndexCacheService` から候補を受け取るだけにする。

表示候補:

- 固定タグ
- 最近使ったタグ
- 件数上位タグ
- 検索入力で絞ったタグ

### 8.2 textbox 補完

中央のタグ追加 textbox も同じ index を使う。

用途:

- 入力補完
- 部分一致候補
- 既存タグの揺れ防止

## 9. 実装フェーズ

### Phase 1: メモリ cache

やること:

- `TagIndexCacheService` 追加
- DB open 後にバックグラウンド構築
- 右タブは cache 参照へ変更
- タグ追加/削除時に差分更新

やらないこと:

- sidecar 永続化
- 件数表示 UI の作り込み

### Phase 2: sidecar DB

やること:

- `TagIndexStore` 追加
- snapshot の保存/読込
- source DB の整合チェック
- 再構築制御

やらないこと:

- `*.wb` の変更

### Phase 3: UI 拡張

やること:

- 全タグ表示
- 件数順/名前順切替
- textbox 補完
- 最近使ったタグ

## 10. リスク

### 10.1 タグ揺れ

`★` と ` ★ ` のような揺れがあると index が汚れる。

対策:

- 保存前に trim
- 空文字除去
- 重複除去

### 10.2 初回構築コスト

DBが大きいと全件走査が重い。

対策:

- UI 初回表示は固定タグだけ
- index 構築はバックグラウンド
- 完成後に差し替え更新

### 10.3 sidecar 不整合

source DB 更新後に sidecar が古い可能性がある。

対策:

- source 最終更新時刻
- movie 件数
- schema version

で無効化判定する

## 11. 推奨案

推奨は次の順で進める。

1. `TagIndexCacheService` をメモリ cache として実装
2. 右タブと textbox 補完を cache 参照へ寄せる
3. 運用上必要になったら sidecar DB を追加

この順なら、`*.wb` を守りながら、体感テンポと拡張性を両立できる。
