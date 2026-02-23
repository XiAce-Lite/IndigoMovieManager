# データベース仕様メモ（SQLite）

## 1. 概要
- DBエンジン: SQLite
- 生成処理: `SQLite.CreateDatabase`
- 主テーブル: `movie`, `bookmark`, `history`, `watch`, `system`

## 2. テーブル一覧
- `movie`: 動画本体の管理情報
- `bookmark`: ブックマーク画像情報
- `history`: 検索履歴
- `findfact`: 検索語の利用回数
- `watch`: 監視フォルダ設定
- `system`: DB単位の設定（キー/値）
- `profile`: スキンごとの表示設定
- `tagbar`: タグバー関連（現状は限定利用）
- `sysbin`: バイナリ設定領域

## 3. `movie` / `bookmark` の主要カラム
- `movie_id`: 主キー
- `movie_name`, `movie_path`: 名称とパス
- `movie_length`, `movie_size`: 長さ（秒）とサイズ
- `last_date`, `file_date`, `regist_date`: 日付情報
- `score`, `view_count`
- `hash`
- `container`, `video`, `audio`, `extra`
- `title`, `artist`, `album`, `genre` などメタ情報
- `tag`, `comment1..3`

## 4. `system` テーブルの利用キー
- `thum`: サムネイル保存先
- `bookmark`: ブックマーク保存先
- `keepHistory`: 検索履歴保持件数
- `playerPrg`: 個別プレイヤー
- `playerParam`: 個別プレイヤーパラメータ

## 5. 主要アクセスパターン
- 一覧取得: `select * from movie ...`
- 単一列更新: `UpdateMovieSingleColumn`
- 検索履歴登録: `InsertHistoryTable`
- 監視設定更新: `DeleteWatchTable` + `InsertWatchTable`

## 6. 運用上の注意
- 一部SQLは文字列連結のため、将来的に全面パラメータ化推奨
- ID採番は `max(id)+1` 方式のため、同時更新には弱い
- `movie_name` は小文字化して保存する実装があるため、検索仕様と合わせて確認が必要

## 7. 関連コード
- `../DB/SQLite.cs`
- `../DB/DbSettings.cs`
- `../WatchWindow.xaml.cs`
