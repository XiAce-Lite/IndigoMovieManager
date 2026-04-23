# AI向け 現在の全体プラン（開発本線） 2026-03-20

最終更新日: 2026-04-23

変更概要:
- 全体計画を `rescue` 主導から `Watcher / UI差分反映` 主導へ再構成した
- 本線の最上位テーマを `Watcher.cs` の薄化、watch change set、diff-first UI へ置き直した
- `rescue` は新規主戦場ではなく、通常動画テンポを壊さないための維持・棚卸しレーンへ再定義した
- 起動 warm path、visible-first 画像供給、`skin` 完全分離を本線後段レーンとして並べ直した
- `SearchService` の `kana / roma / tag split` は `MovieRecords` 単位の遅延キャッシュへ寄せ、検索確定時の全件再計算を減らした
- `SearchService` の通常検索は、term 解釈を先にコンパイルして各行では比較だけを行う形へ寄せた
- `SearchService` の通常検索マッチングは LINQ の `Any/All` 連鎖を手書きループへ寄せ、比較時の delegate / allocation を減らした
- `{dup}` と exact tag / notag も LINQ 連鎖を縮小し、特殊検索での列挙回数と allocation を減らした
- `SearchSidecar` は本線リポから一旦外し、別リポで継続検証する方針へ切り替えた
- 起動 deferred services の `CreateWatcher()` は `ApplicationIdle` へ 1 拍後ろ倒しし、first-page 直後の UI tick を軽くした
- Bookmark 下部タブの再読込は、`bookmark` DB read と `MovieRecords` 生成を background 化し、UI は `ObservableCollection` 反映だけへ寄せた
- 起動時 auto-open の `system` 先読みをコンストラクタ同期読込から外し、cold start 既定値だけ先に入れて `ContentRendered -> TrySwitchMainDb(...)` へ寄せた
- watch existing movie で query-only incremental watch 中かつ `file_date / movie_size` 差分または length 未確定の時だけ metadata probe を許し、`ObservedState.MovieLength` を局所更新へ流せるようにした
- `WatchMainDbMovieSnapshot` に `file_date / movie_size` を追加し、Everything 起点の watch existing movie でも cheap な `DirtyFields` を出せるようにした
- watch query-only 局所更新で `ObservedState` を source `MovieRecords` へ当て、DB 再読込なしでも `file_date / movie_size` 変更が sort/filter に効くようにした
- `{dup}` 検索中に `Hash` を含む changed movie が来た時は changed-path 局所更新を降ろし、full in-memory filter へ戻して重複グループの出入りを取りこぼさないようにした
- さらに通常検索では、dirty fields が検索列に無関係な時は現在の一致状態を再利用し、changed-path 局所更新で per-path `FilterMovies(...)` まで省くようにした
- 空検索では changed movie の種別に関係なく一致判定を省き、watch query-only で per-path `FilterMovies(...)` を完全に避けるようにした
- さらに `!tag` / `!notag` のようなタグ専用検索では、既存一致行に限って現在の一致状態を再利用し、rename 系でも per-path `FilterMovies(...)` を省けるようにした
- さらに非空検索でも search 非依存 dirty の既存行は、現在一致だけでなく現在不一致の状態も再利用し、metadata 更新での per-path `FilterMovies(...)` をもう一段減らした
- さらに sort 再適用も「今の filtered 結果に残る changed movie」だけで判断するようにし、見えていない変更や検索から外れた行では `SortMovies(...)` まで回さないようにした
- watch の Everything 増分 cursor が無い pass では、広域候補列挙による既存DB metadata refresh を止め、`refresh existing-db-metadata` の過剰発火と `MovieInfo` probe 詰まりを避ける安全弁を追加した
- `load/persist last_sync` と `incremental cursor unavailable` のログへ `db / folder / sub / attr` を残し、DB切替直後や cursor 不整合時の原因を `debug-runtime.log` だけで追えるようにした
- `Auto` でも Everything 増分 cursor を読むようにし、cursor なし周回では既存DB metadata refresh 自体を止めて `scanned=大量 -> refresh existing-db-metadata大量発火` の再発を避ける形へ寄せた
- `Manual` / `Watch` を問わず、最終的に full reload へ戻る周回では途中の `repair view by existing-db-movie` を抑え、再読込ボタンや大量周回で既存DB view repair が全件級に積まれないようにした
- 再読込ボタンは `FilterAndSort(true)` と `Manual scan` を直列化し、その間だけ `Watch / EverythingPoll` を抑止して、全件DB再構築と手動全域走査が同時に走らないようにした
- さらに `manual-reload` 抑止解除直後の catch-up `Watch` は積まず、直前の `Manual scan` で十分な場面に `watch_zero_diff reconcile` の全量再走査を重ねないようにした
- 下部 `ThumbnailProgress` タブが非表示の時は snapshot refresh を dirty 記録だけへ寄せ、`CreateThumbAsync` 完了ごとの hidden UI 更新を抑えて `activity=None` の後段負荷を減らし始めた
- UI を含む高速化の抜本改善プランを追加し、P4 を「全面再評価中心から差分反映中心へ変える」軸で補足
- watch query-only reload に `changed paths` を通し、検索結果集合ベースの局所再評価を追加
- watch change set に `ChangeKind` を追加し、empty search の局所復帰で per-path filter をさらに削減
- `DirtyFields` を追加し、rename 系では sort 非依存変更の full sort を回避
- `Docs/Implementation Plan_2026-03-12.md` をルートへ移設し、AI向けの全体計画書として再編
- rescue 単体計画から、開発本線全体の優先順位と着手順が分かる構成へ更新
- 2026-03-20 時点の進行状況を反映し、完了済み / 進行中 / 後続着手を明示
- `ThumbnailCreationService` 系の直近到達点と、以後崩してはいけない境界を追記
- Watcher の UI/DB 分離着手を反映し、P4 の中で「入口の薄化」と「責務分離」を進行中へ更新
- `skin` 切り替え高速化の調査結果を踏まえ、P4 に `refresh` / `catalog` / DB の優先順を追記
- DB 施策は「先頭の決定打」ではなく「第2群の土台施策」と位置づけ直し、単一ライター / cache / shutdown の固定ルールを追加
- `Watcher.cs` の入口・中盤・終端に残っていた `visible gate / scan strategy detail / watch table load failure / full reconcile` の直書きをさらに helper / policy 側へ寄せ、`CheckFolderAsync(...)` を orchestration 専念へ寄せ続けている
- `Everything poll` は low-update 時の間隔延長、watch folder snapshot、eligible 判定再利用、重複 path 除去まで入り、通常周回の判定コストを一段落とした
- `Watcher.cs` はさらに、`context 初期化`、`background scan`、`scan pipeline`、`movie loop`、`pending flush`、`folder completion`、`run finish`、`folder failure recovery result` を helper / runtime 側へ寄せ、入口・中盤・終端を段単位で薄くした
- `Watcher` の flow 制御は `WatchLoopDecision` を共通の戻り値として揃え、`return / break / continue` の判定を helper 経由で追える形へ寄せた
- さらに `watch folder` 解決、`scan 準備`、`movie loop preparation`、`loop decision await/apply`、`folder phase result`、`run finish` 呼び出しを helper 化し、`CheckFolderAsync(...)` は「フォルダを選ぶ -> scan を準備する -> loop / flush / finish を順に流す」形へ近づいた
- `scan strategy 通知` と `scan mode 診断` は runtime 側で束ね、`Watcher.cs` 側は通知実行の入口だけを見る形へ整理した
- `UiHang` オーバーレイの終了時残留は、常時の無通信 timer を主解にせず、owner 付与、caller 側 hide 保証、overlay thread shutdown 強制線の順で直す方針を固定した。無通信 timer は shutdown safety fuse としてのみ扱う

## 1. この文書の目的

- この文書は、開発本線ブランチで AI が今どこを優先して触るべきかを固定するための全体計画書である。
- 判断基準は一貫して、ユーザーが感じるテンポ感を守りながら速くし、同時に安定運用を崩さないことである。
- 検索、並び替え、ページ移動、タブ切り替えなどの明示的なユーザー要求は、watch / rescue / thumbnail / poll などの背後処理より常に優先する。
- 個別機能の詳細計画へ入る前に、まずこの文書で全体の着手順と禁止線を揃える。

## 2. このブランチの立場

- 本ブランチは UI を含む高速化と安定化の**唯一の本線**である。
- 対象は一覧表示、Watcher、Queue、サムネイル生成、救済導線を含む。
- 過去の実験的アプローチ（future 等）の成果は、本線のテンポを壊さない形に一般化されたものだけを維持する。

## 3. 現在の大粒度優先順位

| 優先 | テーマ | 目的 | 状態 |
|---|---|---|---|
| P0 | サムネイル生成入口整理の維持 | `Factory + Interface + Args` の本流を崩さず後続改修を載せる | 完了済み、維持フェーズ |
| P1 | `Watcher / UI差分反映` 本流 | `Watcher.cs` を orchestration へ縮め、watch を `event -> change set -> diff apply` の一本線へ寄せる | 進行中 |
| P2 | 起動 warm path 短縮 | first-page 表示後へ仕事を逃がし、入力可能までの待ちをさらに減らす | 進行中 |
| P3 | visible-first 画像供給 | 可視範囲と無関係な decode / file stamp / metadata miss をさらに減らす | 着手前 |
| P4 | `skin` 表示・保存完全分離 | `refresh / stale / catalog / DB` を分離し、切り替え体感をもう一段細くする | 進行中 |
| P5 | rescue / repair 維持と棚卸し | 通常動画テンポを壊さず、救済系の条件と観測を維持・整理する | 維持フェーズ |

## 4. いま固定する判断基準

1. 最優先はユーザー体感テンポである。
  ユーザーの明示要求は背後処理より先に完了させる。
2. 通常動画の初動を悪化させる変更は、正しさだけでは採用しない。
3. 観測できない高速化は採用しない。最低限のログで理由を追える状態を維持する。
4. 難読動画対応（過去の検証成果含む）は、通常経路の既定動作を重くしない範囲でのみ維持する。
5. 大きい整理は、責務を戻さず薄く載せられる時だけ進める。
6. 検証用 worktree / 退避コピーは本体 repo 直下へ置かず、使用後は必ず削除する。ビルド成功より前に「本体 compile へ混ざらないこと」を優先確認する。
7. 検索等のユーザー要求が走っている間は、watch の full / bulk reload、rescue、thumbnail、poll などの背後処理を必要に応じて遅延・抑止してよい。
8. `UiHang` オーバーレイ残留は `無通信timer` だけで隠さない。owner / lifecycle / shutdown guarantee を先に正し、その後に shutdown 専用 safety fuse を足す。

## 5. 完了済みの土台

### 5.1 P0: サムネイル生成入口整理

- `ThumbnailCreationServiceFactory`
- `IThumbnailCreationService`
- `ThumbnailCreateArgs`
- `ThumbnailBookmarkArgs`
- host 別 factory 分離
- service 本体の facade 化
- `create` / `bookmark` の引数検証を coordinator 側へ集約
- service が concrete coordinator を持たず delegate 2 本だけを保持する形へ整理
- architecture test で factory 境界、validator 境界、legacy 非存在を固定

この土台は完了済みとして扱う。今後の UI / rescue / worker 修正では、この入口整理を崩さないことを前提にする。

### 5.1.1 P0 の現在の禁止線

- `ThumbnailCreationService` に direct constructor や legacy 入口を戻さない
- 引数検証を service 本体へ戻さない
- `MainWindow` / `RescueWorker` から factory を飛び越えて concrete 実装を触らない
- rescue / queue 都合で `Factory + Interface + Args` の公開面を広げない

### 5.2 先行して進んでいる本線の改善

- 上側タブ visible-first 系の高速化は一部着手済み
- ページ移動引っかかり解消も一部着手済み
- 下部タブ分割や大 DB 起動段階ロード化も計画化済み
- Watcher は `Created` / `Renamed` のイベント入口を共通 queue 化し、watch event queue / UI bridge / MainDB writer / rename bridge / registration へ責務分離を開始済み
- `Watcher.cs` から、watch policy / helper だけでなく runtime 側の塊も順次 partial へ切り出し始めている
- `Watcher.cs` の入口では `watch table load failure`、`visible gate`、`scan strategy detail`、`full reconcile` の引数組み立てを順次内側へ寄せ、`CheckFolderAsync(...)` の読み筋をさらに薄くしている
- `Watcher.cs` はさらに `context 初期化`、`background scan`、`movie loop`、`pending flush`、`folder 終端`、`run finish`、`folder failure recovery` を helper / runtime 側へ寄せ、`CheckFolderAsync(...)` の直列処理を段ごとに読める形へ近づけている
- `skin` 切り替えでは、重さの主因が `refresh` 二重化 / stale 判定後ろ倒し / catalog 再走査 / WebView2 再 navigate に寄っていることを確認済み

ただし、いまの本線は rescue を先頭テーマとして広げる段階ではない。最上位優先は `Watcher / UI差分反映` と起動テンポ改善であり、rescue はその副作用を増やさない維持レーンとして扱う。

## 6. 進行中の主計画

## Phase 1: `Watcher / UI差分反映` 本流

### 6.1 目的

- `Watcher.cs` を orchestration に寄せ、watch の結果を `event -> change set -> diff apply` の一本線で扱えるようにする。
- 少数変更でも `FilterAndSort(..., true)` や DB 全件再読込へ戻る構造を崩し、変更件数依存の UI 反映へ寄せる。

### 6.2 最優先確認項目

1. `Watcher.cs` に残す責務を queue orchestration / folder orchestration / final dispatch に絞ること
2. watch 終端の既定経路を `full reload` ではなく `diff apply` 優先へ寄せること
3. `changed paths + ChangeKind + DirtyFields + ObservedState` の change set を UI 反映まで潰さず運べること
4. 大量変更、起動時部分ロード中、`{dup}` のような特殊条件だけを安全側の full 経路へ限定できること

### 6.3 完了条件

- watch 1 件追加や rename で、全面再評価を常に踏まない
- `Watcher.cs` と周辺 partial の責務を短く説明できる
- full reload へ戻る条件を列挙できる

## Phase 2: 起動 warm path 短縮

### 6.4 方針

- first-page を最優先し、その後ろへ送れる処理は `ContentRendered` や `ApplicationIdle` 後へ寄せる
- UI 入力可能前に DB read / watcher 配備 / bookmark 生成を詰め込まない
- 起動を `first-page shown` / `input ready` / `heavy services started` へ分けて扱う

### 6.5 補強候補

- auto-open の後ろ倒し継続
- watcher 起動の idle 寄せ継続
- bookmark / tag / queue warm path の後ろ倒し
- 起動直後に必要な read model と後でよい常駐処理の分離

## 7. 次に着手する計画

## Phase 3: visible-first 画像供給

- `NoLockImageConverter` の miss 経路と stamp 取得の局所化
- viewport 連動の decode / metadata 優先制御
- off-screen 領域の decode 後ろ倒し

着手条件は、Phase 1 と Phase 2 の本流が崩れていないこと。

## Phase 4: `skin` 表示・保存完全分離

重点候補は以下である。

- `skin` 切り替えの `refresh` 起点一本化
- `skin` 切り替えの stale 判定前倒し
- `skin` catalog 再走査の削減
- `skin` 切り替え保存系 DB I/O の UI 経路外し
- API profile read/write の UI snapshot / DB 実行分離
- `session cache` と `persisted` の整合分離

ここでは DB を先頭の決定打として扱わず、`refresh / stale / catalog` の後段施策として扱う。

### 7.1 Phase 4 の直近進捗

- `Created` は直接 MainDB 登録せず、watch 本流の `QueueCheckFolderAsync(CheckMode.Watch, ...)` へ合流済み
- `Renamed` は watch event queue 経由で単一ランナー処理へ変更済み
- `WatcherEventQueue` は処理 task を 1 本共有し、enqueue ごとに runner を増やさない形へ寄せて watch burst 時の先頭詰まり増幅を抑えた
- `Created` の ready 待機は queue runner から分離して直列専用パイプラインへ逃がし、`Renamed` が `Created` 待ちで詰まらないように整合を補強した
- 旧パス未登録の `Renamed` は `QueueCheckFolderAsync(CheckMode.Watch, ...)` へ再合流させ、`Created -> Renamed` 連鎖の取りこぼしを watch 本流で回収する形へ寄せた
- watch 終端の全件 `FilterAndSort(..., true)` は `CheckMode.Watch` 時のみ debounce 済み
- `skin` 切り替えは `ApplySkinByName(...)` からの明示 refresh queue を外し、`DbInfo.Skin` 変化を正本へ寄せた
- `skin` refresh は stale 判定を開始直後、definition 解決後、prepare 中、apply 前へ前倒しした
- `WhiteBrowserSkinCatalogService.Load(...)` は root 単位 cache と signature 再読込判定を追加済み
- `skin` 保存系は `WhiteBrowserSkinStatePersister` を追加し、`system.skin` / `profile.LastUpperTab` / API profile write を単一ライターへ寄せた
- `MainWindow` 起点の `system` 保存のうち、`sort` と個別設定 (`thum` / `bookmark` / `keepHistory` / `playerPrg` / `playerParam`) も同じ persister 優先へ寄せた
- `Watcher` の `last_sync` 保存も同じ persister 優先へ寄せ、通常運用の `system` 直書きをさらに縮小した
- 設定保存直後の `GetSystemTable(...)` 再読込は外し、`systemData` / `MainVM.DbInfo` を in-memory で先に揃える形へ修正した
- `skin` 保存 shutdown は `writer complete -> drain -> timeout 時だけ cancel` へ変更した
- shutdown 開始後の `system` direct fallback は止め、`Everything` poll 停止を writer completion より先へ寄せた
- 外部 skin API の `getProfile` は UI snapshot と DB 読み取りを分離し、`SelectProfileValue(...)` の UI スレッド滞在を減らし始めた
- `skin` profile cache は `pending / persisted / faulted` を分離し、API 即時整合と初期タブ復元整合を分けて扱い始めた
- 起動時 auto-open の `system` 先読みはコンストラクタで同期実行せず、cold start 既定値だけ入れて `ContentRendered` 後の `TrySwitchMainDb(...)` に一本化した
- Bookmark 下部タブの再読込は、表示中でも `bookmark` DB read と `MovieRecords` 生成を background 化し、UI スレッドにはコレクション反映だけを残した
- 起動 deferred services の `CreateWatcher()` は `ApplicationIdle` へ後ろ倒しし、first-page 表示直後の light services を少し薄くした
- 監視系コードは次の partial へ分割済み
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `Watcher/MainWindow.WatcherEventQueue.cs`
  - `Watcher/MainWindow.WatcherUiBridge.cs`
  - `Watcher/MainWindow.WatcherMainDbWriter.cs`
  - `Watcher/MainWindow.WatcherRenameBridge.cs`
  - `Watcher/MainWindow.WatchScanCoordinator.cs`
- `CheckFolderAsync` 内の `pendingNewMovies` flush は `WatchScanCoordinator` へ移し、`MainDB登録 -> 小規模UI反映 -> enqueue` の塊を本流から外し始めた
- `CheckFolderAsync` 内の per-file `new/existing` 分岐も `ProcessScannedMovieAsync(...)` として `WatchScanCoordinator` へ寄せ始めた
- `CheckFolderAsync` の入口では `watch table load failure`、`visible gate`、`scan strategy detail + strategy log`、`full reconcile user-priority` を helper / policy 1 呼び出しへまとめ、`Watcher.cs` 側の引数直書きと一時変数をさらに減らした
- `CheckFolderAsync` の入口・中盤・終端では、`context 初期化`、`background scan`、`scan pipeline`、`movie loop`、`pending flush`、`final queue flush`、`run finish`、`folder failure recovery result` も helper / runtime 1 呼び出しへ寄せ、`Watcher.cs` 側の直書きと局所 if をさらに減らした
- 直近では `watch folder` 解決、`PrepareWatchFolderScanAsync(...)`、`TryBeginWatchFolderMovieLoop(...)`、`AwaitAndApplyWatchLoopDecisionAsync(...)`、`TryHandleWatchFolderPhaseResult(...)`、`TryFinishWatchRunAndReturnAsync(...)` を入れ、入口から終端までの flow 判定を同じ形へそろえた
- watch query-only reload は `ChangedMoviePaths` を deferred reload まで保持し、`RefreshMovieViewFromCurrentSourceAsync(...)` で `FilteredMovieRecs` から changed paths だけ抜き差しして再評価する初手まで入った
- さらに `WatchChangedMovie(ChangeKind)` を通し、`SourceInserted` / `ViewRepaired` / `DisplayedViewRefresh` は empty search 時に直接復帰できるようになった
- rename も `WatchChangedMovie(ChangeKind + DirtyFields)` に寄せ、`MovieName / MoviePath / Kana` 変更でも current sort 非依存なら既存順を再利用できるようになった
- さらに `Watcher.cs` から、scope / background scan / last sync I/O / thumbnail queue helper / UI suppression runtime / deferred scan runtime / scan strategy / rescue runtime / scan DTO を partial へ切り出し始めた
- さらに `WatchLoopDecision` を `movie loop` と `pending flush` の共通戻り値として使い、`return / UI suppression で break / continue` を同じ流れで読めるようにした
- `WatchMainDbMovieSnapshot(file_date / movie_size)` と `WatchMovieObservedState` を追加し、Everything 起点の watch existing movie では cheap な file 属性差分を `DirtyFields` として局所更新へ流せるようになった
- query-only 局所更新では `ObservedState` を `MovieRecords` へ先に当ててから filter / sort 判定へ進め、DB 再読込なしでも `file_date / movie_size` 変更が反映されるようになった
- さらに query-only incremental watch 中で、cheap 差分または DB length 未確定の時だけ metadata probe を許し、watch existing movie の `MovieLength` 変更も局所更新へ流せるようになった
- そのうえで `{dup}` 検索だけは `Hash` 変化時に changed-path 局所更新を使わず、full in-memory filter へ戻す安全弁を入れた
- そのうえで通常検索では `MovieSize / FileDate / MovieLength` など検索非依存 dirty の時、changed path ごとの `FilterMovies(...)` 呼び出しも省くようにした
- さらに本線の通常検索では、`MovieRecords` 側へ検索投影 cache を持たせて `kana / katakana / roma / normalized tags` の再生成回数を減らし、既存 `SearchService` 正本のまま hot path を軽くした
- `Everything poll` は watch folder 一覧を snapshot 化し、eligible 判定結果の再利用と重複 path 除去も入れ、low-update 時の間隔延長と合わせて通常周回の無駄判定を減らした
- 検索窓は 1 文字ごとの即時実行を常時有効にはせず、通常時だけ `0.5s debounce -> query-only 検索確定`、起動時部分ロード・IME変換中・途中構文(`-` / `|` / `{`)では Enter 確定へ寄せる形へ戻した
- 検索確定中は `user priority` スコープを張り、`Auto / Watch` の再走査、`watch_zero_diff reconcile`、`missing-thumb rescue` を後ろへ逃がして、明示的なユーザー要求を先に完了させる導線を入れた
- さらに `FilterAndSortAsync(...)` の観測点を `db-reload / source-apply / filter-movies / sort-movies / replace-filtered` へ細分化し、検索 hot path の詰まり位置を実機ログで断定できるようにした
- さらに通常検索の比較は、ASCII 系検索語だけ `OrdinalIgnoreCase` の軽い比較へ寄せ、日本語など非 ASCII を含む語は従来どおり `CurrentCultureIgnoreCase` を維持して `filter-movies` の hot path を軽くし始めた
- さらに ASCII 検索では `Movie_Name / Movie_Path / Tags / Comment1-3 / Roma` だけを見る軽量投影 cache を使い、`kana / katakana` 派生列の全件生成を避けて `filter-movies` の詰まりを減らし始めた
- 実機ログでは `ggggg` のような ASCII 検索で `filter-movies` 詰まりが出ていたが、軽量投影 cache 追加後は検索完了まで進むことを確認した。ASCII 検索の主因は比較方式そのものより `kana / katakana` 派生列の全件生成だったと判断する
- さらに textbox 入力の重さは `SearchBox_TextChanged(...)` ごとの `RestartThumbnailTask()` 連打が主因だったため、通常入力中はサムネ常駐を再起動せず、実検索の瞬間だけ再起動する形へ寄せた
- `UiHangNotificationCoordinator` と `NativeOverlayHost` の停止経路を確認し、オーバーレイ残留は「owner なし native popup を別スレッド dispatcher 依存で止めていること」が主因候補と整理した。直し方は `owner 付与 -> caller 側即 hide -> join timeout 後の強制閉鎖 -> shutdown 専用 safety fuse` の順に固定する

### 7.2 Phase 4 の次の着手順

1. `CheckFolderAsync` にまだ残る小粒のローカル関数、runtime context 生成後の引数受け渡し、`final dispatch` 手前の局所分岐を、テンポを落とさない範囲でさらに外へ出す
2. watch event DTO と queue 処理を `MainWindow` 依存からさらに離し、`WatcherEventDispatcher` 相当へ寄せる
3. watch 起点の UI 再読込を、差分反映優先でさらに縮小できる箇所を切り分ける
  現在は `changed paths + ChangeKind + DirtyFields + ObservedState` ベースの局所 filter / 直接復帰 / rename reuse-order / existing movie file属性反映 / query-only incremental watch時の必要時限定probe / `{dup}` 時の安全fallback まで。次は `Hash` を safe に局所反映できる条件を見極め、watch existing movie の局所 sort 回避条件をさらに広げる
4. 検索高速化は一旦本線リポ外で検証する。`SearchSidecar` は別リポで継続し、本線では既存 `SearchService.FilterMovies(...)` 正本を維持する
  ただし本線内での hot path 軽量化として、既存 `SearchService` の検索投影 cache 化は継続してよい
5. `UiHang` オーバーレイ残留は、`NativeOverlayHost` に owner を持たせ、終了時は caller 側から即 hide を保証し、overlay thread join timeout 後は強制閉鎖まで行う。常時の無通信 timer は採らず、最後に shutdown 専用 safety fuse だけを評価する

### 7.2.1 Phase 4 の抜本方針

### 7.2.2 検証用 worktree / 退避コピーの再発防止

- `HEAD` や commit hash 名の退避コピーを repo 直下へ置くと、SDK 既定取り込みで `.xaml` / `.g.cs` / `.cs` が本体 compile へ混ざり、`IComponentConnector.Connect(int, object)` の重複実装のような壊れ方を起こす。
- 検証用 worktree は必ず `C:\Users\na6ce\source\repos\IndigoMovieManager-worktree-*` のように sibling ディレクトリへ作る。
- 退避コピーや検証用フォルダは「使い終わったら削除」を完了条件に含める。
- `IndigoMovieManager.csproj` 側の `HEAD\**\*` / commit hash フォルダ除外は保険として維持してよいが、正本の再発防止は「repo 直下へ置かない運用」に置く。

- `Views/Main/MainWindow.xaml.cs` の `FilterAndSortAsync(...)` を中心に残っている「少数変更でも全面再評価へ戻る構造」を崩し、一覧 UI を差分反映中心へ寄せる。
- watch、画像供給、起動、skin 切り替えは個別最適でなく、この軸に沿って進める。
- 詳細は `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md` を正本とする。
- 検索高速化の別リポ検証は継続してよいが、本線へ戻す時は既存検索仕様との整合と fallback 条件を先に固める。

### 7.3 Phase 4 における `skin` 切り替え DB 方針

`skin` 切り替え高速化では、DB は主因そのものではなく **第2群の土台施策** として扱う。

固定する実施順は次である。

1. `refresh` 起点を 1 本化する
2. stale 判定を host 準備より前へ寄せる
3. `WhiteBrowserSkinCatalogService.Load(...)` の常時再走査を止める
4. その後で保存系 DB write を UI 経路から外す
5. API 側 profile 読み書きの UI snapshot / DB 実行分離を進める
6. profile 読み取りの cache / 非同期化は、整合条件を固めてから最後に進める

DB 施策で固定する設計ルールは次である。

1. `system.skin` / `profile.LastUpperTab` / 外部 skin API の profile write は、最初から同一 persister に統合する
2. `profile` の session cache は enqueue 時に正本化しない。persist 成功反映か、少なくとも dirty / fault を区別する
3. shutdown は `writer complete -> bounded drain -> timeout 時だけ cancel` を原則にし、cancel を先に打たない
4. `SelectProfileValue(...)` の扱いは保存分離より後に置き、初期表示の整合を崩さない設計が固まるまで安易に async 化しない
5. DB 分離だけで体感改善完了と見なさず、`refresh` / stale / catalog 改善とセットで評価する

この領域の完了条件は次とする。

1. `skin` 切り替え 1 回で不要な `refresh` が実質 1 回へ近づいている
2. catalog 再走査が常時発生しない
3. DB write が UI スレッドの同期ボトルネックとして残っていない
4. profile 保存の最終状態と session 内の見え方が乖離しない

## Phase 5: 難読動画条件の棚卸し

ここでは rescue を新規主戦場として広げず、維持・観測・棚卸しに寄せる。

- repair が走った条件
- repair が走らなかった条件
- `No frames decoded` で救えた条件
- `No frames decoded` でも救えなかった条件
- `ERROR` マーカー固定へ落ちた条件
- 明示 UI を足すとしても、新ロジックではなく既存 rescue レーンの入口追加に留める

ここでは新分岐を増やす前に、条件を動画名ではなく一般条件へ圧縮する。

## 8. 今回見送るもの

- FailureDb の全面導入（必要最小限に留める）
- worker 分離や IPC の本格導入
- coordinator 群の丸移植
- 個別動画名ベースの新分岐追加
- UI テンポ改善の名目で観測性を削る変更

## 9. AI が変更前に必ず確認すること

- この変更は一覧、Watcher、Queue、サムネ生成、救済導線のどこを速くするのか
- 通常動画の初動を重くしていないか
- UI スレッドへ重い処理を戻していないか
- ログだけで遅くなった理由、救済へ行った理由を追えるか
- 難読動画対応が通常経路の既定動作を重くしていないか
- 既に分離した `Factory + Interface + Args` の境界を壊していないか
- `ThumbnailCreationService` を再びオーケストレータ本体へ戻していないか

## 10. 受け入れ判断

### 10.1 rescue / queue

- 通常動画の初動を壊していない
- timeout handoff と failure handoff を区別して追える
- repair 条件を一般条件で説明できる
- `ERROR` マーカー削除挙動を説明できる

### 10.2 UI

- 変更前より体感テンポが良い、または少なくとも悪化していない
- 一覧、ページ移動、再読込のどこに効いたか説明できる
- `skin` 切り替えでは `refresh` / catalog / DB のどこが効いたかを分けて説明できる

### 10.3 アーキテクチャ

- 新しい direct constructor を増やしていない
- 責務を `MainWindow` や `ThumbnailCreationService` に戻していない
- validator と coordinator の責務分離を壊していない
- delegate facade と host 別 factory の境界を壊していない
- `skin` の profile write 経路が複数ライターへ再分岐していない

## 11. 関連資料

- `C:\Users\na6ce\source\repos\IndigoMovieManager\AI向け_ブランチ方針_ユーザー体感テンポ最優先_2026-04-07.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\Docs\現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\Docs\優先順位表_失敗9件の検証順_2026-04-07.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\UpperTabs\Docs\Implementation Plan_上側タブvisible-first高速化_2026-03-15.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\UpperTabs\Docs\Implementation Plan_ページUpDown引っかかり解消_2026-03-18.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Views\Main\Docs\Implementation Plan_大DB起動段階ロード化_2026-03-17.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\Implementation Plan_下部タブ分割_Phase1_サムネ進捗_2026-03-15.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Watcher\調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\WhiteBrowserSkin\Docs\調査結果_skin切り替え重さの原因_2026-04-12.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`
