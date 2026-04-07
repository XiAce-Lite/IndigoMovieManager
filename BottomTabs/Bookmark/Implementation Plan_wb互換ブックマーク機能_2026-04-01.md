# Implementation Plan_wb互換ブックマーク機能_2026-04-01

最終更新日: 2026-04-01

変更概要:
- WhiteBrowser 逆アセンブリ仕様と現行 Bookmark 実装の差分から、WB 互換ブックマークの実装順を整理
- 互換の中核を「保存先規約」「索引の持ち方」「シーン再生」「削除/rename 整合」に分解
- `.wb` スキーマ非変更、起動テンポ優先の前提を固定

## 1. 目的

- WhiteBrowser 互換のブックマークを「作れる / 一覧できる / 正しい動画の正しい秒へ戻れる / 消せる」状態まで固める。
- WhiteBrowser の DB (`*.wb`) のスキーマは変更しない。
- 本線方針どおり、起動時 first-page と通常動画のテンポを落とさない。

## 2. 根拠

- `.local/WhiteBrowser逆アセンブリ仕様完成_2026-04-01/WhiteBrowser逆アセンブリ仕様書_2026-04-01.md`
  - `bookmark` は「実際の画像ファイルを指す索引」として使われる。
  - 既定のブックマークルートは `bookmark/<DB名>/`。
  - シーンジャンプは画像側の WB 互換メタを使う。
- `BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs`
  - 現在は `Directory.GetCurrentDirectory()/bookmark/<DB名>` を既定にしている。
  - 追加時は画像生成を fire-and-forget し、その直後に DB 行を追加している。
  - 再生時に必要な「元動画の厳密な特定情報」を持っていない。
- `Views/Main/MainWindow.Player.cs`
  - Bookmark 再生は `Movie_Body` 部分一致で元動画を探しており、同名系で誤爆しうる。
  - 再生秒は `score / FPS` に依存しており、WB 互換メタを使っていない。
- `DB/SQLite.cs`
  - `InsertBookmarkTable(...)` は bookmark 行へ最小列しか保存していない。
  - `DeleteBookmarkTable(...)` は DB 行だけを消し、画像ファイルは消していない。
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbRootResolver.cs`
  - `thum` 側だけは WB 互換の既定根 resolver がある。
  - bookmark 側には同等の resolver がまだ無い。

## 3. 現状差分

1. Bookmark 保存先の fallback が WB 互換になっていない。
2. Bookmark 追加が非同期 fire-and-forget で、画像生成失敗時に壊れた DB 行が残りうる。
3. Bookmark 行に元動画の厳密な identity が無く、再生が部分一致頼みになっている。
4. 再生秒が bookmark 画像メタではなく `score` 流用になっている。
5. 削除時に bookmark 画像が孤児化する。
6. 一覧再構築がファイル名の `Split('[')` / `Split('(')` に依存していて壊れやすい。
7. rename 時の bookmark 追従が文字列置換寄りで、対象の厳密性が弱い。

## 4. 今回固定する方針

- WB 互換の正本は「bookmark 画像 + bookmark テーブル」とみなす。
- `movie_name` と `movie_path` は WB 互換表示に必要な bookmark 画像索引として維持する。
- Indigo 側が再生復元に必要な元動画 identity は、既存列へ保存してスキーマ変更を避ける。
- 再生秒は bookmark JPEG 末尾の WB 互換メタを正本とし、旧データだけ暫定 fallback を持つ。
- 起動時挙動は今の deferred 読込を維持し、Bookmark を理由に first-page を重くしない。

## 5. Bookmark 行の内部契約

新規作成 bookmark について、Indigo 側の内部契約を次で固定する。

- `movie_name`
  - bookmark 画像のベース名。WB 互換表示を維持する。
- `movie_path`
  - bookmark 画像の相対ファイル名 (`xxx.jpg`)。WB 互換表示を維持する。
- `comment1`
  - 元動画のフルパス。
- `comment2`
  - 元動画の `movie_id`。
- `comment3`
  - 元動画の hash。空でもよい。

補足:

- bookmark コメント UI は現状無いので、まずは既存 `comment1-3` を内部メタ用途に使う。
- 将来「bookmark 自体のコメント編集」を入れたくなった時だけ、別保存方式へ再設計する。

## 6. 実装フェーズ

### Phase 1: 保存先とデータ契約の固定

目的:

- WB 互換のルート解決と、bookmark 1 件の保存契約を先に固定する。

実装:

1. `BookmarkRootResolver` を追加する。
2. 解決順を次で統一する。
3. `system.bookmark` が入っていれば最優先で使う。
4. `system.bookmark` が空で、DB と同じフォルダに `WhiteBrowser.exe` があれば `dbDir/bookmark/<DB名>` を使う。
5. それ以外は `AppContext.BaseDirectory/bookmark/<DB名>` を使う。
6. `ResolveBookmarkFolderPath()` はこの resolver 呼び出しへ置き換える。

受け入れ条件:

- `system.bookmark` 空の WB 同居 DB で、bookmark 出力先が `dbDir/bookmark/<DB名>` になる。
- 通常 DB は従来どおり本 exe 側の `bookmark/<DB名>` を使う。

### Phase 2: Bookmark 追加を壊れない形へ寄せる

目的:

- 画像生成成功後にだけ DB 登録する。
- 新規 bookmark が厳密再生できる状態で保存されるようにする。

実装:

1. `AddBookmark_Click(...)` は `CreateBookmarkThumbAsync(...)` 完了を待つ。
2. 画像生成成功時だけ `InsertBookmarkTable(...)` を呼ぶ。
3. DB 保存値は 5 章の内部契約で埋める。
4. 旧来の `score` へフレームを入れる契約は、新規分からは主契約にしない。
5. 一覧再読込は成功後に 1 回だけ流す。

受け入れ条件:

- 画像生成失敗時に bookmark 行が増えない。
- 新規 bookmark 行に `comment1-3` が入り、元動画 identity を失わない。

### Phase 3: 一覧読込と再生を WB 互換メタ基準へ寄せる

目的:

- `Movie_Body` 部分一致依存をやめ、bookmark 画像メタと内部 identity を使って正しく再生する。

実装:

1. `GetBookmarkTable()` の filename 文字列分解を helper 化する。
2. 元動画の解決順を次で固定する。
3. `comment2` の `movie_id` 一致。
4. `comment1` のフルパス一致。
5. 旧データ向けの legacy fallback として、現行の `Movie_Body` 部分一致を最後にだけ残す。
6. bookmark 再生秒は `ThumbInfo.GetThumbInfo(bookmarkJpegPath)` から取る。
7. 旧 bookmark 画像に WB 互換メタが無い時だけ `score / FPS` fallback を許す。

受け入れ条件:

- 新規 bookmark は同名動画が複数あっても正しい元動画へ戻る。
- 新規 bookmark は `score` に依存せず正しい秒へ戻る。
- 旧 bookmark だけ legacy fallback が効き、完全破壊を避ける。

### Phase 4: 削除と rename の整合

目的:

- bookmark が DB だけ、画像だけで片残りしないようにする。

実装:

1. 削除時は bookmark 行から画像相対パスを解決し、bookmark ルート配下だけを best-effort で削除する。
2. 画像削除後に DB 行を削除する。
3. 元動画 rename 時は `comment2/comment1/comment3` を正本に対象 bookmark を特定する。
4. 必要なら bookmark 画像名と DB 索引名も同じ helper で追従させる。
5. 文字列部分一致ベースの rename は縮小する。

受け入れ条件:

- 削除後に DB 行と bookmark 画像が両方消える。
- rename で無関係な bookmark を巻き込まない。

### Phase 5: 旧 bookmark 修復

目的:

- 既存 bookmark を一気に壊さず、新契約へ寄せる逃がし道を持つ。

実装:

1. 旧 bookmark はそのまま読めるようにする。
2. 元動画を厳密に特定できた旧 bookmark だけ、明示操作または安全なタイミングで `comment1-3` を補完する。
3. 自動修復は「誤爆しない条件」だけに限定する。

受け入れ条件:

- 旧 bookmark を読めなくしない。
- 誤った元動画へ自動ひも付けしない。

## 7. 今回の非スコープ

- `wb.getBookmarks / wb.execBookmark / wb.deleteBookmark` の JS API 互換実装
- `#DefaultBookmark` 相当の HTML/JS Extension 移植
- KMPlayer キャプチャ自動取込
- bookmark コメント編集 UI
- `.wb` スキーマ変更

## 8. テスト方針

- Unit test
  - `BookmarkRootResolver`
  - bookmark 行の metadata 読み書き helper
  - bookmark 再生元の解決順
  - bookmark 秒位置の WB メタ優先 / legacy fallback
- Integration test
  - 追加成功時だけ DB 行が増える
  - 削除で画像と DB が両方消える
  - rename で対象 bookmark だけが追従する
- Manual test
  - WB 同居 DB
  - 通常 DB
  - 同名動画が複数あるケース
  - 旧 bookmark 混在 DB

## 9. リスクと対策

- `comment1-3` を内部メタへ使うため、将来 bookmark コメント UI と衝突する可能性がある。
  - 当面は UI 非搭載なので採用し、必要になった時点で再設計する。
- 旧 bookmark 修復を自動化し過ぎると誤爆する。
  - 新規 bookmark の厳密化を先に入れ、旧 bookmark は保守的に扱う。
- Bookmark 読込を重くすると起動テンポが落ちる。
  - 既存の deferred 読込は維持し、first-page 経路には載せない。

## 10. 着手順

1. `BookmarkRootResolver` を追加し、bookmark 既定根を WB 互換へ寄せる。
2. bookmark 追加を「画像成功後に DB insert」へ変更する。
3. bookmark 行の内部メタ契約を固定する。
4. bookmark 再生を `comment1-3 + ThumbInfo` 基準へ寄せる。
5. 削除と rename を厳密化する。
6. 最後に旧 bookmark 修復の要否を判断する。

## 11. 受け入れ判断

- 新規 bookmark が WB 互換ルートへ保存される。
- 新規 bookmark が正しい元動画の正しい秒へ戻る。
- 削除で孤児画像を残さない。
- `.wb` スキーマを変えない。
- 起動時 first-page と通常動画のテンポを悪化させない。
