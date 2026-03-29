# 調査結果 Lane B MainDB入口棚卸し 2026-03-20

最終更新日: 2026-03-20

## 1. 目的と止めどころ

- 今回は `Lane B: Data 入口集約` の初動3着手を決めるための棚卸しだけに止める。
- 対象は MainDB 直アクセスのうち、実装役が追加調査なしで facade 化の着手点を決められる粒度に絞る。
- `*.wb` schema 議論、QueueDB / FailureDb の全面設計、全件完全網羅は今回の対象外とする。

## 2. MainDB read 入口一覧

| 入口 | 現在位置 | 役割 | ひとこと |
|---|---|---|---|
| 起動時 first-page / append-page 読み出し | `Startup/StartupDbPageReader.cs:14` | `movie` から必要列だけを page 単位で読む | 起動表示専用 read。Data DLL 側の最初の read facade 候補。 |
| 登録件数ヘッダー取得 | `Views/Main/MainWindow.xaml.cs:181` | `select count(*) from movie` | 軽いが UI が直接 SQL を握っている。 |
| system 読み出し | `Views/Main/MainWindow.xaml.cs:1146` | `system` 全件読取で skin / sort / 各種設定復元 | DB切替時の必須 read。 |
| history 読み出し | `Views/Main/MainWindow.xaml.cs:1100` | `history` を検索候補用に読込 | UI 検索補助だが MainWindow 直保持。 |
| watch 読み出し | `Views/Main/MainWindow.xaml.cs:1207` | `watch` テーブル読込 | movie 本流より side table 色が強い。 |
| watch 有効フォルダ確認 | `Views/Main/MainWindow.xaml.cs:829` | `select dir from watch where watch = 1` | 起動時の監視可否判定。 |
| 一覧フル再読込 | `Views/Main/MainWindow.xaml.cs:1354` / `:1379` | `SELECT * FROM movie` で全件再取得 | 量も責務も大きい read 入口。 |
| watch 用 existing snapshot | `Watcher/MainWindow.WatcherMainDbWriter.cs:33` | `movie_id, movie_path, hash` を辞書化 | watcher 専用 read として切り出しやすい。 |
| watch 小規模差分の1件再読込 | `Watcher/MainWindow.WatcherUiBridge.cs:14` | `movie_path` 指定で `movie` 1件取得 | 本来は UI bridge ではなく read facade 経由に寄せたい。 |
| rescue worker の thumb 設定読取 | `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs:4466` | `system.attr='thum'` だけ読む | 既に read-only 前提。 worker 側はこの姿を維持したい。 |
| watch の Everything 同期点読取 | `Watcher/MainWindow.Watcher.cs:1879` | `system` から watch 用 last sync を読む | watcher special read。 system facade 側で吸いたい。 |

## 3. MainDB write 入口一覧

| 入口 | 現在位置 | 役割 | ひとこと |
|---|---|---|---|
| watch バッチ登録 | `Watcher/MainWindow.WatcherMainDbWriter.cs:11` | `InsertMovieTableBatch(...)` で `movie` へ複数 INSERT | watcher 専用 writer facade の本命。 |
| 単一 movie 列更新の共通口 | `DB/SQLite.cs:504` | `UpdateMovieSingleColumn(...)` | UI 各所がここを直叩きしている。第3優先の集約対象。 |
| movie 削除 | `DB/SQLite.cs:629` | `DeleteMovieTable(...)` | UI 操作から直接呼ばれている。 |
| system 保存 | `DB/SQLite.cs:546` | `UpsertSystemTable(...)` | sort / skin / player / thumb / watch sync が同じ口に乗る。 |
| history 追加 / findfact 追加 / history 削除 | `DB/SQLite.cs:1322` / `:1427` / `:691` / `:1390` | 検索履歴系の side write | movie 本流と分けて扱うべき。 |
| watch 設定保存 | `DB/SQLite.cs:445` / `:471` | `watch` 全消し + 再登録 | WatchWindow が UI から直接保存している。 |
| bookmark 追加 / 再生回数 / rename / 削除 | `DB/SQLite.cs:1509` / `:1571` / `:1599` / `:1635` | `bookmark` side write | これも movie 本流と別 facade の方が扱いやすい。 |

## 4. UI 直叩きの箇所

### 4.1 movie テーブル直更新

- `Views/Main/MainWindow.Tag.cs:79`, `:150`, `:218`, `:289`
  - タグ更新から `UpdateMovieSingleColumn(...)` を直接呼ぶ。
- `UserControls/TagControl.xaml.cs:134`
  - TagControl からも `UpdateMovieSingleColumn(...)` を直接呼ぶ。
- `Views/Main/MainWindow.Player.cs:146`, `:147`, `:153`
  - 再生時に `score` / `view_count` / `last_date` を直接更新する。
- `Views/Main/MainWindow.MenuActions.cs:78`, `:134`, `:427`
  - rename に伴う `movie_path` 更新、score 更新、movie 削除を直接呼ぶ。
- `Thumbnail/MainWindow.ThumbnailCreation.cs:367`
  - サムネ作成完了時に `movie_length` を直接更新する。
- `Watcher/MainWindow.WatcherRenameBridge.cs:31`, `:37`
  - watch rename でも `movie_path` / `movie_name` を直接更新する。

### 4.2 system / history / bookmark / watch の UI 直叩き

- `Views/Main/MainWindow.xaml.cs:1269`, `:1297`
  - sort / skin を `UpsertSystemTable(...)` で直接保存。
- `Views/Main/MainWindow.MenuActions.cs:867-891`
  - thumb / bookmark / keepHistory / player 設定を `UpsertSystemTable(...)` で直接保存。
- `Views/Main/MainWindow.Search.cs:87-89`, `:209`, `:235`
  - 検索時に `InsertHistoryTable(...)` / `InsertFindFactTable(...)` / `DeleteHistoryTable(...)` を直接呼ぶ。
- `BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs:178`, `:227`
  - bookmark 追加 / 削除を UI から直接呼ぶ。
- `Views/Main/MainWindow.Player.cs:64`
  - bookmark 再生回数更新を UI から直接呼ぶ。
- `Watcher/WatchWindow.xaml.cs:37`, `:42`
  - WatchWindow close 時に `watch` テーブルを全消しして再登録する。

## 5. watch 専用 writer 化しやすい箇所

- 第1候補は `Watcher/MainWindow.WatcherMainDbWriter.cs:11`
  - `InsertMoviesToMainDbBatchAsync(...)` が既に watch 専用 partial に寄っている。
  - 呼び出し側も `Watcher/MainWindow.WatchScanCoordinator.cs:28` に集まり始めている。
- 第2候補は `Watcher/MainWindow.WatcherMainDbWriter.cs:33`
  - `BuildExistingMovieSnapshotByPath(...)` は watcher 以外の責務が薄く、read side をまとめやすい。
- 第3候補は `Watcher/MainWindow.Watcher.cs:1855` / `:1879`
  - Everything 用 last sync の `system` read/write は watcher 文脈が明確で、後から watcher settings facade へ切り出しやすい。

補足:
- `Watcher/MainWindow.WatcherUiBridge.cs:14` の 1件再読込は writer ではなく read / UI bridge 境界に残す方が自然。
- つまり watcher 側は `read snapshot` と `batch insert` を先に DLL へ寄せ、UI 反映は別境界で持つのが順番として良い。

## 6. worker 側の read-only 化候補

- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs:4466`
  - 現在の MainDB 接触は `system.attr='thum'` の取得だけで、`CreateReadOnlyMainDbConnection(...)` も明示されている。
- このため worker 側は「read-only 化候補」ではなく、すでに read-only 化済みの良い例として扱うのが正しい。
- 次の着手は、新しい MainDB 参照が worker に増える時も
  - read-only 接続
  - `system` / 必要最小限列だけ
  - `movie` 更新はしない
  を固定すること。

## 7. special case

### 7.1 `InsertMovieTable(...)` / `InsertMovieTableBatch(...)` は純粋 writer ではない

- `DB/SQLite.cs:725` と `:860` の実体は INSERT だけではない。
- どちらも `DB/SQLite.cs:748` と `:928` で `TryReadBySinkuDll(...)` を呼び、
  - `movie_length`
  - `container`
  - `video`
  - `audio`
  - `extra`
  を登録時に同時補完している。
- つまり facade 化の時は `MovieInsertWriter` と `MovieMetadataProbe` を分ける前提で考えないと、Data DLL へ寄せても重い責務が残る。

### 7.2 `watch` / `history` / `bookmark` / `system` は movie 本流と分ける

- いずれも MainDB には入っているが、`movie` CRUD と責務が違う。
- 最初の facade で全部を一つにまとめると、逆に責務が太る。
- 初動は
  - `movie read`
  - `movie write`
  - `system/watch settings`
  - `history/bookmark`
  の分離を前提にした方が実装役が迷わない。

## 8. 最初に facade 化すべき 3 入口

### 1位: MainWindow の movie read 入口

- 対象:
  - `Startup/StartupDbPageReader.cs:14`
  - `Views/Main/MainWindow.xaml.cs:181`
  - `Views/Main/MainWindow.xaml.cs:1146`
  - `Views/Main/MainWindow.xaml.cs:1354`
- 理由:
  - 起動、DB切替、一覧再読込が UI 体感テンポに直結している。
  - read が `MainWindow` 本体と startup reader に分散しており、Data DLL へ寄せる効果が最も見えやすい。
- facade の叩き台:
  - `IMainDbMovieReadFacade`
  - `LoadStartupPage`
  - `LoadMainWindowBootstrap`
  - `LoadMovieListForSort`

### 2位: watcher の movie read/write 入口

- 対象:
  - `Watcher/MainWindow.WatcherMainDbWriter.cs:11`
  - `Watcher/MainWindow.WatcherMainDbWriter.cs:33`
- 理由:
  - 既に partial 分離が進んでいて、Data DLL へ寄せる境界が見えている。
  - 将来の watch 専用 single writer 化にもそのまま繋がる。
- facade の叩き台:
  - `IWatchMainDbFacade`
  - `LoadExistingMovieSnapshot`
  - `InsertMoviesBatch`

### 3位: UI から散っている単一 movie 更新入口

- 対象:
  - 実体は `DB/SQLite.cs:504`
  - 呼び出し散在先は `Views/Main/MainWindow.Tag.cs`, `Views/Main/MainWindow.Player.cs`, `Views/Main/MainWindow.MenuActions.cs`, `Thumbnail/MainWindow.ThumbnailCreation.cs`, `Watcher/MainWindow.WatcherRenameBridge.cs`, `UserControls/TagControl.xaml.cs`
- 理由:
  - update の窓口自体は1つだが、呼び出し元が UI 全域に散っている。
  - ここを facade にすると、後続で `movie` 更新権限を UI から剥がしやすい。
- facade の叩き台:
  - `IMovieMutationFacade`
  - `UpdateTag`
  - `UpdatePlayState`
  - `RenameMovie`
  - `UpdateMovieLength`
  - `DeleteMovie`

## 9. 実装役への短い指示

- 最初は `movie read` と `watch movie read/write` と `movie single update` だけを facade 化対象に固定する。
- `system` / `history` / `bookmark` / `watch` は MainDB 内ではあるが、初動3件とは別レーンとして切る。
- watcher の INSERT facade を作る時は、同時に `Sinku` 補完責務を writer から分ける余地を必ず残す。
