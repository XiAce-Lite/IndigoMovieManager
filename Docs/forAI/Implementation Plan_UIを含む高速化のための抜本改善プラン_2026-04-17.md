# Implementation Plan UIを含む高速化のための抜本改善プラン 2026-04-17

最終更新日: 2026-04-23

変更概要:
- 文書の主軸を `rescue` や個別機能の列挙ではなく、`Watcher / UI差分反映` 主導の実行レーンへ組み替えた
- この文書の役割を「本線で今進める実行レーンと完了条件の正本」に絞り、全体プランとの役割重複を減らした
- `rescue / repair` は新規主戦場ではなく、通常動画テンポを壊さないための維持レーンへ再定義した
- `SearchService` の `kana / roma / tag split` は `MovieRecords` 単位の遅延キャッシュへ寄せ、検索確定時の全件再計算を減らした
- `SearchService` の通常検索は、term 解釈を先にコンパイルして各行では比較だけを行う形へ寄せた
- `SearchService` の通常検索マッチングは LINQ の `Any/All` 連鎖を手書きループへ寄せ、比較時の delegate / allocation を減らした
- `{dup}` と exact tag / notag も LINQ 連鎖を縮小し、特殊検索での列挙回数と allocation を減らした
- 起動 deferred services の `CreateWatcher()` は `ApplicationIdle` へ 1 拍後ろ倒しし、first-page 直後の UI tick を軽くした
- Bookmark 下部タブの再読込は、`bookmark` DB read と `MovieRecords` 生成を background 化し、UI は `ObservableCollection` 反映だけへ寄せた
- 起動時 auto-open の `system` 先読みをコンストラクタ同期読込から外し、cold start 既定値だけ先に入れて `ContentRendered -> TrySwitchMainDb(...)` へ寄せた
- UI を含む高速化を、個別最適ではなく「全面再評価中心」から「差分反映中心」へ切り替える全体方針として整理
- `FilterAndSort`、watch 終端 reload、画像 I/O、skin 切り替え、起動導線を 1 本の計画で接続
- WhiteBrowser DB (`*.wb`) を変更せず、sidecar / cache / coordinator でテンポを上げる前提を明文化
- watch query-only reload に `changed paths` を通し、`FilteredMovieRecs` の局所再評価で全件 filter を避ける初手を追記
- watch change set に `ChangeKind` を追加し、`empty search + view repair/source insert` では per-path filter を省く現在地を追記
- `DirtyFields` を追加し、rename 系では「検索再判定は必要でも current sort に無関係なら既存順を再利用する」現在地を追記
- `WatchMainDbMovieSnapshot(file_date / movie_size)` と `WatchMovieObservedState` を追加し、Everything 起点の watch existing movie でも cheap な file 属性差分を局所更新へ流せるようにした
- watch existing movie で query-only incremental watch 中かつ `file_date / movie_size` 差分または length 未確定の時だけ metadata probe を許し、`ObservedState.MovieLength` を局所更新へ流せるようにした
- `{dup}` 検索中に `Hash` を含む changed movie が来た時は changed-path 局所更新を降ろし、full in-memory filter へ戻して重複グループの出入りを取りこぼさないようにした
- さらに通常検索では、dirty fields が検索列に無関係な時は現在の一致状態を再利用し、changed-path 局所更新で per-path `FilterMovies(...)` まで省くようにした
- さらに空検索では changed movie の種別に関係なく一致判定を省き、watch query-only で per-path `FilterMovies(...)` を完全に避けるようにした
- さらに `!tag` / `!notag` のようなタグ専用検索では、既存一致行に限って現在の一致状態を再利用し、rename 系でも per-path `FilterMovies(...)` を省けるようにした
- さらに非空検索でも search 非依存 dirty の既存行は、現在一致だけでなく現在不一致の状態も再利用し、metadata 更新での per-path `FilterMovies(...)` をもう一段減らした
- さらに sort 再適用も「今の filtered 結果に残る changed movie」だけで判断するようにし、見えていない変更や検索から外れた行では `SortMovies(...)` まで回さないようにした
- watch の Everything 増分 cursor が無い pass では、既存DB metadata refresh を止める安全弁を追加し、DB切替直後や cursor 不整合時の広域再観測で `FileDate` dirty と `MovieInfo` probe が大量発火しないようにした
- あわせて `load/persist last_sync` と `incremental cursor unavailable` のログに `db / folder / sub / attr` を出し、`debug-runtime.log` だけで cursor 不整合を追えるようにした
- さらに `Auto` でも Everything 増分 cursor を読むようにし、cursor なし周回では既存DB metadata refresh を止めて、広域候補収集がそのまま `FileDate` dirty 大量発火へ繋がらないようにした
- 再読込ボタンや大量変更で最終的に full reload へ戻る周回では、途中の `repair view by existing-db-movie` を止め、最終 `full reload` に一本化してログ氾濫と無駄な局所反映を避けるようにした
- 再読込ボタンは `full filter-sort` と `Manual scan` を並走させず、watch 抑止下で直列化して `FilterAndSort(true) + Manual scan + EverythingPoll` の三重化を避けるようにした
- さらに `manual-reload` 抑止解除直後の catch-up `Watch` は積まず、再読込直後に `watch_zero_diff reconcile` が全量再走査を重ねる二度踏みを止めるようにした
- 下部 `ThumbnailProgress` タブが非表示の時は snapshot refresh を即時 UI 更新せず dirty 記録だけへ寄せ、サムネ成功直後の hidden progress 更新が `activity=None` を増やす経路を細くし始めた
- `SearchSidecar` は本線リポから一旦外し、別リポで継続検証する方針へ切り替えた
- 本線の検索 hot path は、sidecar を使わず既存 `SearchService` 正本のまま `MovieRecords` 単位 cache で軽量化する方針へ寄せた
- 検索窓のインクリメント検索は、常時即時実行ではなく `0.5s debounce` で通常時だけ戻し、起動時部分ロード・IME変換中・途中構文では Enter 確定へ寄せる
- さらに検索確定中は `user priority` スコープで `Auto / Watch` の再走査、`watch_zero_diff reconcile`、`missing-thumb rescue` を defer し、検索完了を背後処理より先に通す
- さらに検索詰まりの切り分け用に、`FilterAndSortAsync(...)` の観測点を `db-reload / source-apply / filter-movies / sort-movies / replace-filtered` まで分解し、実機ログだけで hot path を断定できるようにした
- さらに通常検索の比較は、ASCII 系検索語だけ `OrdinalIgnoreCase` の軽い比較へ寄せ、日本語など非 ASCII を含む語は従来どおり `CurrentCultureIgnoreCase` を維持して `filter-movies` の hot path を軽くし始めた
- さらに ASCII 検索では `Movie_Name / Movie_Path / Tags / Comment1-3 / Roma` だけを見る軽量投影 cache を使い、`kana / katakana` 派生列の全件生成を避けて `filter-movies` の詰まりを減らし始めた
- 実機確認では `ggggg` のような ASCII 検索で `filter-movies` が完了しない事象を再現し、軽量投影 cache 追加後は検索完了まで進むことを確認した。ASCII 検索 hot path の主因は、比較順より `kana / katakana` 派生列の全件生成だったと整理する
- さらに textbox 入力の重さは `SearchBox_TextChanged(...)` ごとの `RestartThumbnailTask()` 連打が主因だったため、通常入力中はサムネ常駐を再起動せず、実検索の瞬間だけ再起動する形へ寄せた
- `UiHang` オーバーレイの終了時残留は、常時の無通信 timer を主解にせず、owner 付与、caller 側 hide 保証、overlay thread shutdown 強制線の順で解く方針を固定した。無通信 timer は shutdown 専用 safety fuse としてのみ扱う
- `Watcher.cs` は入口と中盤の `watch table load failure`、`visible gate`、`scan strategy detail`、`full reconcile` 入口判定を helper / policy 側へ寄せ続け、`CheckFolderAsync(...)` を orchestration 専念へさらに寄せた
- `Everything poll` は watch folder snapshot、eligible 判定再利用、重複 path 除去、low-update 時の間隔延長まで入り、通常周回の CPU / wakeup コストを下げ始めた
- `Watcher.cs` はさらに、`context 初期化`、`background scan`、`scan pipeline`、`movie loop`、`pending flush`、`folder completion`、`run finish`、`folder failure recovery result` を helper / runtime 側へ寄せ、入口・中盤・終端を段単位で薄くした
- `WatchLoopDecision` を `movie loop` と `pending flush` の共通戻り値へ揃え、`return / break / continue` の flow を同じ読み筋で追える形へ寄せた
- `scan strategy 通知` と `scan mode 診断` は runtime 側で束ね、`Watcher.cs` 側は orchestration と通知入口に専念する形へ整理した

## 1. 目的

- ユーザーが最初に触る一覧、検索、ページ移動、watch 反映、skin 切り替えの体感テンポを、局所改善ではなく構造変更で底上げする。
- 検索、並び替え、ページ移動、タブ切り替えなどの明示的なユーザー要求は、watch / rescue / thumbnail / poll などの背後処理より最優先で完了させる。
- 高速化の主戦場を「重い処理を少し速くする」から、「重い処理をそもそも起こさない」へ移す。
- 通常動画の初動を守りながら、rescue / queue / watcher / skin の既存本線と矛盾しない着手順を固定する。

## 2. いまの見立て

本丸は仮想化不足ではない。主因は、少数変更でも全面再評価へ戻る構造が複数箇所に残っていること。

### 2.1 一覧再評価

- `Views/Main/MainWindow.xaml.cs:1560` の `FilterAndSortAsync(...)` は、DB再読込、source 差し替え、全件 filter/sort、`Refresh()` までを 1 本で持つ。
- `Infrastructure/SearchExecutionController.cs` と `Views/Main/MainWindow.Search.cs` では、通常の検索確定を `query only recompute` 側へ寄せ始めている。`RefreshMovieViewAfterRenameAsync(...)` も、rename 後の一覧再計算をメモリ上 read model だけで回す初手まで入っている。
- `Infrastructure/SearchService.cs` は、検索仕様の正本を維持したまま `MovieRecords` 単位の遅延 cache を使い、`kana / katakana / roma / normalized tags` を毎回再生成しない形へ寄せた。
- 一方で、起動直後の部分ロード中は full reload を維持する意味論が残っており、`query only recompute` と `full snapshot reload` の境界はまだ育成中である。
- `Watcher/MainWindow.Watcher.cs`、`Watcher/MainWindow.WatcherUiBridge.cs`、`Watcher/MainWindow.WatcherRenameBridge.cs`、`Watcher/MainWindow.WatchScanCoordinator.cs` では、watch 後の最終 reload と rename 後追従を軽量化し始めているが、大量変更時や起動時部分ロード中は full reload へ戻す境界をまだ整理中である。
- 直近では、watch 側で `changed paths + ChangeKind` を集約し、`Views/Main/MainWindow.xaml.cs` の in-memory refresh へ渡して「現在の `FilteredMovieRecs` から changed paths だけ抜き差しして再検索する」経路を追加した。これで検索結果が総件数より十分小さい時は、watch query-only でも全件 filter を避けられる。
- さらに `empty search` かつ `source insert / view repair / displayed refresh` の時は、per-path `FilterMovies(...)` すら省いて直接復帰できる現在地まで入った。
- rename 系では `MovieName / MoviePath / Kana` の dirty fields を明示し、current sort がそれらに依存しない時は full sort を避けて既存順を再利用する現在地まで入った。
- watch existing movie でも、Everything 起点の changed path に限っては `file_date / movie_size` の cheap な観測値を `ObservedState` として source `MovieRecords` へ当て、DB 再読込なしで局所更新へ載せる現在地まで入った。
- さらに query-only incremental watch 中で cheap 差分または DB length 未確定の時だけ metadata probe を許し、watch existing movie の `MovieLength` 変更も `ObservedState` 経由で局所更新へ載せる現在地まで入った。
- ただし `{dup}` 検索だけは changed path 外の既存行も結果へ出入りするため、`Hash` 変化時は changed-path 局所更新を使わず full in-memory filter へ戻す安全弁を入れた。
- その一方で通常検索では、`MovieSize / FileDate / MovieLength` など検索非依存 dirty の時は changed path ごとの `FilterMovies(...)` も省き、現在の一致状態をそのまま再利用する現在地まで入った。
- つまり「変更件数は少ないのに、結果として一覧全体を考え直す」経路が残っている。

### 2.2 画像表示

- `Infrastructure/Converter/NoLockImageConverter.cs:51` 以降は改善済みだが、miss 時は `FileInfo` と decode を踏む。
- `Infrastructure/Converter/NoLockImageConverter.cs:292` の metadata miss は往復スクロールやページ移動でまだ効く。
- visible-first は進んだが、「今見えている範囲だけを優先する」思想が一覧全体の更新経路までは貫通していない。

### 2.3 skin 切り替え

- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:97` の apply は簡潔になったが、skin 解決、初期タブ解決、persist、host refresh が近接している。
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:232` では definition 解決時に catalog load を伴う。
- catalog cache と refresh scheduler は入ったが、skin 切り替え全体を「表示切替」と「保存・整合」に完全分離し切ったとはまだ言えない。

### 2.4 起動と常駐開始

- 起動段階ロード化で first-page 化は進んだが、起動後の watch / bookmark / queue / skin 関連の warm path はまだ分散している。
- 直近では、起動時 auto-open の `system` 先読みをコンストラクタから外し、最初の表示前は cold start 既定値だけを使って `ContentRendered` 後の DB 切替へ寄せた。
- さらに Bookmark 下部タブの再読込も、`bookmark` DB read と item 生成を background 化し、UI スレッドには結果反映だけを残し始めた。
- さらに起動 deferred services の `CreateWatcher()` も `ApplicationIdle` へ後ろ倒しし、first-page 直後の UI tick に watch table 読込と watcher 配備を詰め込まないようにした。
- warm start をさらに詰めるには、起動直後に必要な read model と、後で良い常駐処理をより明確に分ける必要がある。

### 2.5 `UiHang` オーバーレイ終了残留

- `Views/Main/UiHangNotificationCoordinator.cs` の `Stop()` は `_overlayHost.Hide(); _overlayHost.Stop();` を呼ぶが、hide と shutdown の実体は別スレッド dispatcher への依頼中心である。
- `Views/Main/NativeOverlayHost.cs` は native overlay を owner なしの `CreateWindowExW(...)` で作っており、本体ウインドウ終了へ OS の owner-chain を使って追従していない。
- `Views/Main/NativeOverlayHost.cs` の `Stop()` は join timeout 後に `overlay thread still alive after shutdown request` で諦めうるため、終了競合時に HWND が取り残される余地がある。
- したがって主因は「表示中の overlay 自体」より、「overlay の寿命管理が owner なし + overlay thread 正常応答前提」な点にある。
- `無通信timer` は見た目を減らす safety fuse にはなるが、overlay thread 側が詰まると効かず、UI 本当に詰まり中でも誤って hide しうるため主解にはしない。

## 3. 抜本方針

結論は 1 つ。

**一覧 UI を「全面再評価UI」から「差分反映UI」へ変える。**

この方針を成立させるため、この文書では以下の実行レーンで進める。

1. Lane 0: 計測固定
2. Lane 1: `Watcher.cs` 本体の薄化完了
3. Lane 2: watch change set と diff-first UI の一本化
4. Lane 3: 起動 warm path の再短縮
5. Lane 4: visible-first 画像供給の徹底
6. Lane 5: `skin` 切り替えの表示・保存完全分離
7. Lane 6: rescue / repair 維持レーン

## 4. 非機能の固定ルール

1. WhiteBrowser DB (`*.wb`) のスキーマは変更しない。
2. sidecar は補助であり正本にしない。壊れても fallback で戻れることを前提にする。
3. UI スレッドへ重い処理を戻さない。
  ただしユーザー要求を先に通すための背後処理抑止・延期は積極的に採る。
4. 高速化のために観測性を削らない。
5. rescue / repair / queue の既定動作を重くしない。
6. 検索の正本は既存 `SearchService` に置き、本線ではここを基準に保守する。
7. `UiHang` オーバーレイ残留は `無通信timer` だけで隠さない。owner / lifecycle / shutdown guarantee を正してから、最後に shutdown 専用 fuse を足す。

## 5. 実行レーン

## Lane 0: 計測の固定

目的:
- 改善前後を感覚ではなく数値で比較できるようにする。

実施内容:
- `filter start/end`、watch reload、page append、thumbnail decode、skin refresh の trace id を揃える。
- 指標を `debug-runtime.log` で横断して読めるようにする。
- 最低限の計測点を固定する。
  - 起動: `ContentRendered -> first-page shown`
  - 一覧: `search input -> filtered apply end`
  - watch: `event accepted -> ui diff applied`
  - skin: `apply requested -> host presented`
  - 画像: `viewport request -> image ready`

完了条件:
- どこが遅いかを「起動」「一覧」「watch」「skin」「画像」で分けて説明できる。

## Lane 1: `Watcher.cs` 本体の薄化完了

目的:
- `Watcher.cs` を orchestration に寄せ、watch の本流を `queue orchestration / folder orchestration / final dispatch` へ縮める。
- pure policy、runtime helper、storage helper、DTO を外へ出し、watch 改修時の読み解きコストを下げる。

実施内容:
- `Watcher/MainWindow.Watcher.cs` から、scope / background scan / last sync / thumbnail queue / UI suppression / deferred scan / rescue / DTO の塊を partial へ切り出し続ける。
- `CheckFolderAsync(...)` に残る `visible-only gate / zero-byte / first-hit 通知 / final queue flush / queue runner入口` を coordinator / policy / runtime へ寄せる。
- `QueueCheckFolderAsync(...)` と `ProcessCheckFolderQueueAsync(...)` の入口を薄くし、mode 圧縮や dispatcher 判断を専用 helper へ寄せる。
- 直近到達点として、`watch table load failure`、`visible gate`、`scan strategy detail + strategy log`、`full reconcile user-priority` に加え、`context 初期化`、`background scan`、`scan pipeline`、`movie loop`、`pending flush`、`run finish`、`folder failure recovery result` も helper / runtime 1 呼び出しへ寄せ終わっている。次は runtime context 生成後の小粒な受け渡しと `final dispatch` 手前の局所分岐をさらに減らす。

完了条件:
- `Watcher.cs` の責務を短く説明できる。
- watch の純粋判定と状態管理が `Watcher.cs` 本体に貼り付かない。
- watch の入口変更時に、影響範囲を partial 単位で追える。

## Lane 2: watch change set と diff-first UI の一本化

目的:
- 一覧更新時の仕事量を「総件数依存」から「変更件数依存」へ寄せる。
- watch、rename、検索変更で同じ全面再評価経路へ戻る構造を崩す。

実施内容:
- `MainWindow` 直下にある検索条件、ソート、上側タブ状態、ページ状態を `QueryState` として 1 か所へ寄せる。
- `movieData -> MovieRecords[] -> FilteredMovieRecs` の都度組み立てをやめ、read model 更新と view query 適用を分離する。
- `isGetNew=true` の full reload と、検索語変更・ソート変更・watch 差分反映を同じ入口で扱わない。
- まず以下の 3 種に分ける。
  - full snapshot reload
  - query only recompute
  - item diff apply
- `MainVM.ReplaceFilteredMovieRecs(...)` を中核にしつつ、一覧反映の前段に `FilteredMovieDiffCoordinator` 相当を置く。
- watch / rescue / manual reload の反映を「追加」「削除」「更新」「順位変更」に分ける。
- `FilterAndSort(..., true)` を watch の既定終端から外し、小規模変更は差分 apply を既定にする。
- watch query-only では `MovieRecs` 全件を毎回 `FilterMovies(...)` に通さず、`changed paths` だけを再評価して `ReplaceFilteredMovieRecs(...)` へ渡す経路を育てる。
- 検索等のユーザー要求中は、watch full / bulk reload、zero-diff reconcile、rescue、thumbnail などが完了を妨げないよう後ろへ逃がす。
- 全面再評価が必要な条件だけを明示する。
  - sort key 変更
  - query 条件変更
  - 大量変更しきい値超過
  - DB 切り替え
- 検索高速化の別リポ検証は継続してよいが、本線へ戻す時は既存検索仕様と fallback 条件を先に揃える。

完了条件:
- watch の 1 件追加や rename で `Refresh()` 全面経路を常に踏まない。
- 「小規模差分」と「全面再評価」の境界がコード上で説明できる。

## Lane 3: 起動 warm path の再短縮

目的:
- first-page を最優先し、その後ろへ送れる処理は `ContentRendered` や `ApplicationIdle` 後へ寄せる。
- 起動完了を `first-page shown / input ready / heavy services started` に分け、UI が触れるまでの待ちを縮める。

実施内容:
- 起動時 read model を first-page 用と background append 用に明確分離する。
- `CreateWatcher()`、bookmark reload、tag / queue warm path を UI 入力可能後へ順次開始する。
- `OpenDatafile(...)` 後に必要な同期仕事をさらに削り、「表示」「操作可能」「常駐起動完了」を別イベントとして扱う。
- warm start 用の補助 cache を使う場合も `LocalAppData` 配下に限定し、壊れても DB fallback に戻せる形を守る。
- `Everything poll` は watch folder 一覧の snapshot と eligible 判定再利用を前提にし、DB 切替や監視フォルダ編集時だけ invalidation する。通常周回では毎回 `watch` テーブルと同一 path の eligibility を掘り直さない。

完了条件:
- 起動完了を 1 点ではなく、3 段階のイベントで説明できる。
- 大 DB でも first-page 直後の入力待ちがさらに短くなる。

## Lane 4: Visible-first 画像供給の徹底

目的:
- 一覧スクロール、ページ Up/Down、詳細表示で「見える範囲に関係ない I/O」を減らす。

実施内容:
- `NoLockImageConverter` の metadata cache を viewport 連動で活かし、表示候補の先読みと無効化を分ける。
- visible range 外の decode をより後ろへ倒し、可視範囲だけ即時 decode する。
- 画像存在確認と file stamp 取得を、converter 個別呼び出しから `ThumbnailStampCache` 相当へ寄せる。
- 詳細パネル、タグ、bookmark などの補助 UI は、表示された時だけ decode / bind を始める。

完了条件:
- ページ Up/Down 時の体感引っかかりが、cache miss 頻度とともに下がる。
- off-screen 領域の decode が visible 領域を押しのけない。

## Lane 4.5: `UiHang` オーバーレイ寿命管理の是正

目的:
- 終了後に overlay が取り残される事象を、見た目のごまかしではなく寿命管理の正攻法で止める。

実施内容:
- `NativeOverlayHost` の native overlay を MainWindow owner 付き popup として生成し、本体終了へ OS レベルで追従させる。
- `StopUiHangNotificationSupport()` からの停止では、overlay thread dispatcher 依頼より前に caller 側から即 hide を保証する。
- overlay thread の join timeout 後は、`InvokeShutdown()` 任せで終わらせず、強制閉鎖線を持つ。
- そのうえで最後の保険として、shutdown 開始後だけ効く stale fuse を検討する。常時の無通信 timer は入れない。

完了条件:
- `MainWindow` 終了後に overlay が残らない。
- overlay 残留対策が、平常時の UI hang 通知誤抑止を生まない。
- `debug-runtime.log` だけで `hide request -> stop requested -> thread destroyed` まで追える。

## Lane 5: `skin` 切り替えの表示・保存完全分離

目的:
- skin 切り替えを UI テンポ視点でさらに細くし、見た目更新と保存の干渉を切る。

実施内容:
- `ApplySkinByName(...)` は「表示切替要求の確定」までに責務を絞り、persist は非同期経路の completion へ完全移譲する。
- current definition 解決、tab state 解決、persist request、host refresh request を trace 単位で分離する。
- catalog load は起動時 snapshot と変更検知に寄せ、単純な apply で掘り直さない条件を増やす。
- `SelectProfileValue(...)` を cold path に閉じ込め、session cache と persisted 値の責務差を明示する。

完了条件:
- skin 切り替え 1 回で、不要な catalog / DB / refresh の重なりがさらに減る。
- `skin-webview`、`skin-catalog`、`skin-db` を同じ trace で追える。

## Lane 6: rescue / repair 維持レーン

目的:
- rescue / repair を新規主戦場として広げず、通常動画テンポを壊さない範囲で維持・棚卸しする。

実施内容:
- repair が走った条件 / 走らなかった条件を観測し、動画固有名ではなく一般条件へ圧縮する。
- `No frames decoded` から救えた条件と救えなかった条件を整理する。
- UI 追加が必要でも、新ロジックを増やすより既存 rescue レーンの入口追加で留める。

完了条件:
- rescue の挙動を通常動画テンポと切り離して説明できる。
- rescue の変更が本線の hot path を重くしない。

## 6. 優先順位

実装順は次で固定する。

1. Lane 0 計測固定
2. Lane 1 `Watcher.cs` 本体の薄化完了
3. Lane 2 watch change set と diff-first UI の一本化
4. Lane 3 起動 warm path の再短縮
5. Lane 4 Visible-first 画像供給
6. Lane 5 `skin` 切り替え完全分離
7. Lane 6 rescue / repair 維持レーン

理由:
- 先に `Watcher` 本体と一覧側の全面再評価構造を崩さないと、watch や skin を個別最適化しても効果が頭打ちになるため。

## 7. 直近の着手順

### Step 1

- `Watcher.cs` に残っている入口責務を棚卸しし、`queue orchestration`、`folder orchestration`、`final dispatch` 以外を外へ出す。
- 直近では `watch table load failure`、`visible gate`、`scan strategy detail`、`full reconcile` 入口だけでなく、`context 初期化`、`background scan`、`scan pipeline`、`movie loop`、`pending flush`、`run finish` まで helper 化が進んだ。次は runtime context 組み立て後の小粒な受け渡し整理へ進む。

### Step 2

- `FilterAndSortAsync(...)` の呼び出し元を棚卸しし、`full reload`、`query recompute`、`watch reload` の 3 群へ分類する。

### Step 3

- `MovieChangeSet` と `QueryState` の最小 DTO を追加し、watch 終端の既定経路を差分 apply 優先へ寄せる。
- 直近到達点として、`changed paths + ChangeKind + DirtyFields + ObservedState` を使う query-only / rename / watch existing movie 局所更新経路と、query-only incremental watch 時の必要時限定 metadata probe、`{dup}` 時の安全fallback は導入済みである。次は `Hash` を cheap に安全判定できる条件を見極め、`SortMovies(...)` 全体再整列をさらに減らす。

### Step 4

- 起動 warm path と visible-first 画像供給のどちらを先に切るかを、`first-page shown` と `viewport request -> image ready` の計測で決める。

### Step 5

- `UiHang` オーバーレイ残留は、`NativeOverlayHost` の owner 化、caller 側 hide 保証、overlay thread 強制閉鎖線の順に進め、無通信 timer は shutdown 専用 fuse としてのみ最後に評価する。

## 8. 受け入れ基準

1. 通常動画の初動を悪化させない。
2. watch 1 件追加時に全面 reload を常に踏まない。
3. 検索入力やページ移動の引っかかりが、既存ログ比較で改善している。
4. skin 切り替え時の refresh / catalog / DB のどこが効いたか分けて説明できる。
5. sidecar が壊れても起動不能にならない。

## 9. 今回やらないこと

- `*.wb` のスキーマ変更
- IPC や別プロセス化の先行導入
- rescue / repair 条件の拡張を主目的にした高速化
- 仮想化パネル差し替えだけで解決したことにする議論

## 10. 関連資料

- `C:\Users\na6ce\source\repos\IndigoMovieManager\AI向け_現在の全体プラン_workthree_2026-03-20.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forAI\調査結果_UIボトルネック解消_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forAI\調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Views\Main\Docs\Implementation Plan_大DB起動段階ロード化_2026-03-17.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\UpperTabs\Docs\Implementation Plan_ページUpDown引っかかり解消_2026-03-18.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`
