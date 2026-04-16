# AI向け 現在の全体プラン（開発本線） 2026-03-20

最終更新日: 2026-04-17

変更概要:
- watch existing movie で query-only incremental watch 中かつ `file_date / movie_size` 差分または length 未確定の時だけ metadata probe を許し、`ObservedState.MovieLength` を局所更新へ流せるようにした
- `WatchMainDbMovieSnapshot` に `file_date / movie_size` を追加し、Everything 起点の watch existing movie でも cheap な `DirtyFields` を出せるようにした
- watch query-only 局所更新で `ObservedState` を source `MovieRecords` へ当て、DB 再読込なしでも `file_date / movie_size` 変更が sort/filter に効くようにした
- `{dup}` 検索中に `Hash` を含む changed movie が来た時は changed-path 局所更新を降ろし、full in-memory filter へ戻して重複グループの出入りを取りこぼさないようにした
- さらに通常検索では、dirty fields が検索列に無関係な時は現在の一致状態を再利用し、changed-path 局所更新で per-path `FilterMovies(...)` まで省くようにした
- 空検索では changed movie の種別に関係なく一致判定を省き、watch query-only で per-path `FilterMovies(...)` を完全に避けるようにした
- さらに `!tag` / `!notag` のようなタグ専用検索では、既存一致行に限って現在の一致状態を再利用し、rename 系でも per-path `FilterMovies(...)` を省けるようにした
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

## 1. この文書の目的

- この文書は、開発本線ブランチで AI が今どこを優先して触るべきかを固定するための全体計画書である。
- 判断基準は一貫して、ユーザーが感じるテンポ感を守りながら速くし、同時に安定運用を崩さないことである。
- 個別機能の詳細計画へ入る前に、まずこの文書で全体の着手順と禁止線を揃える。

## 2. このブランチの立場

- 本ブランチは UI を含む高速化と安定化の**唯一の本線**である。
- 対象は一覧表示、Watcher、Queue、サムネイル生成、救済導線を含む。
- 過去の実験的アプローチ（future 等）の成果は、本線のテンポを壊さない形に一般化されたものだけを維持する。

## 3. 現在の大粒度優先順位

| 優先 | テーマ | 目的 | 状態 |
|---|---|---|---|
| P0 | サムネイル生成入口整理の維持 | `Factory + Interface + Args` の本流を崩さず後続改修を載せる | 完了済み、維持フェーズ |
| P1 | 救済レーン実動画検証 | 通常動画の初動を壊さず rescue が正しく流れるか固める | 進行中 |
| P2 | Queue 観測の最小補強 | handoff、repair、marker 制御をログで追えるようにする | P1 に従属 |
| P3 | `ERROR` 動画向け明示 UI | 一括救済と単体救済の入口を追加する | 未着手 |
| P4 | UI テンポ改善 | 一覧更新、ページ移動、再読込、タブ描画、Watcher入口の詰まりを軽くする | 進行中 |
| P5 | 難読動画条件の棚卸し | rescue / repair / OpenCV 条件を一般化して整理する | 後続 |

## 4. いま固定する判断基準

1. 最優先はユーザー体感テンポである。
2. 通常動画の初動を悪化させる変更は、正しさだけでは採用しない。
3. 観測できない高速化は採用しない。最低限のログで理由を追える状態を維持する。
4. 難読動画対応（過去の検証成果含む）は、通常経路の既定動作を重くしない範囲でのみ維持する。
5. 大きい整理は、責務を戻さず薄く載せられる時だけ進める。

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

### 5.2 先行して進んでいる UI 側の改善

- 上側タブ visible-first 系の高速化は一部着手済み
- ページ移動引っかかり解消も一部着手済み
- 下部タブ分割や大 DB 起動段階ロード化も計画化済み
- Watcher は `Created` / `Renamed` のイベント入口を共通 queue 化し、watch event queue / UI bridge / MainDB writer / rename bridge / registration へ責務分離を開始済み
- `skin` 切り替えでは、重さの主因が `refresh` 二重化 / stale 判定後ろ倒し / catalog 再走査 / WebView2 再 navigate に寄っていることを確認済み

ただし、いまの最上位優先は rescue 系の副作用確認であり、UI の大改修を先に広げる段階ではない。

## 6. 進行中の主計画

## Phase 1: 救済レーン実動画検証

### 6.1 目的

- 救済レーンが存在することではなく、通常運用を壊していないことを確認する。
- 特に通常動画の初動、timeout handoff、failure handoff、repair 条件の広がり過ぎを確認する。

### 6.2 最優先確認項目

1. 通常動画で `thumbnail-timeout`、`thumbnail-recovery`、`thumbnail-rescue` が不要に出ないこと
2. 重動画で通常レーン `10` 秒 timeout 後に rescue へ handoff されること
3. 通常失敗動画で failure handoff が 1 回だけ発火すること
4. repair 対象だけで `thumbnail-repair probe` / `repair` が出ること
5. 手動等間隔サムネイル作成で stale `ERROR` マーカー削除後に rescue へ入ること
6. error プレースホルダ表示動画が通常キューへ戻らず rescue に隔離されること

### 6.3 完了条件

- 通常動画の初動劣化なしを説明できる
- timeout handoff と failure handoff の差を説明できる
- repair 条件を一般条件で言語化できる
- `ERROR` マーカー削除の発火箇所を説明できる

## Phase 2: Queue 観測の最小補強

### 6.4 方針

- 新しい観測基盤は足さない
- 実動画検証で迷う箇所だけにログを足す
- hot path を広く重くしない

### 6.5 補強候補

- timeout handoff の投入元と投入先
- failure handoff の失敗理由
- repair を見送った理由
- `ERROR` マーカー削除の成否
- error プレースホルダ起点救済の件数

## 7. 次に着手する計画

## Phase 3: `ERROR` 動画向け明示 UI

- `サムネ失敗` タブ
- `サムネイル救済処理` ボタン
- 右クリック `サムネイル救済...`

着手条件は、Phase 1 で通常系を壊していない説明がついていること。
ここで足すのは新しい救済ロジックではなく、既存 rescue レーンへの明示入口である。

## Phase 4: UI テンポ改善

重点候補は以下である。

- 一覧更新の全件差し替え縮小
- ページ Up / Down 時の引っかかり解消
- visible-first の優先制御継続
- 起動直後や再読込時の UI 詰まり低減
- `skin` 切り替えの `refresh` 起点一本化
- `skin` 切り替えの stale 判定前倒し
- `skin` catalog 再走査の削減
- `skin` 切り替え保存系 DB I/O の UI 経路外し
- Watcher の `FileChanged` / `FileRenamed` 入口薄化
- Watcher の `watch event queue` / `UI bridge` / `MainDB writer` / `rename bridge` / `registration` 分離
- watch 終端の `FilterAndSort(..., true)` の debounce 維持と次段の coordinator 化
- 下部タブの責務分割
- 大 DB 起動段階ロード化

ただし、rescue 系の副作用切り分けを難しくする大規模な UI 更新経路変更は、Phase 1 より先に広げない。

### 7.1 Phase 4 の直近進捗

- `Created` は直接 MainDB 登録せず、watch 本流の `QueueCheckFolderAsync(CheckMode.Watch, ...)` へ合流済み
- `Renamed` は watch event queue 経由で単一ランナー処理へ変更済み
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
- 監視系コードは次の partial へ分割済み
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `Watcher/MainWindow.WatcherEventQueue.cs`
  - `Watcher/MainWindow.WatcherUiBridge.cs`
  - `Watcher/MainWindow.WatcherMainDbWriter.cs`
  - `Watcher/MainWindow.WatcherRenameBridge.cs`
  - `Watcher/MainWindow.WatchScanCoordinator.cs`
- `CheckFolderAsync` 内の `pendingNewMovies` flush は `WatchScanCoordinator` へ移し、`MainDB登録 -> 小規模UI反映 -> enqueue` の塊を本流から外し始めた
- `CheckFolderAsync` 内の per-file `new/existing` 分岐も `ProcessScannedMovieAsync(...)` として `WatchScanCoordinator` へ寄せ始めた
- watch query-only reload は `ChangedMoviePaths` を deferred reload まで保持し、`RefreshMovieViewFromCurrentSourceAsync(...)` で `FilteredMovieRecs` から changed paths だけ抜き差しして再評価する初手まで入った
- さらに `WatchChangedMovie(ChangeKind)` を通し、`SourceInserted` / `ViewRepaired` / `DisplayedViewRefresh` は empty search 時に直接復帰できるようになった
- rename も `WatchChangedMovie(ChangeKind + DirtyFields)` に寄せ、`MovieName / MoviePath / Kana` 変更でも current sort 非依存なら既存順を再利用できるようになった
- `WatchMainDbMovieSnapshot(file_date / movie_size)` と `WatchMovieObservedState` を追加し、Everything 起点の watch existing movie では cheap な file 属性差分を `DirtyFields` として局所更新へ流せるようになった
- query-only 局所更新では `ObservedState` を `MovieRecords` へ先に当ててから filter / sort 判定へ進め、DB 再読込なしでも `file_date / movie_size` 変更が反映されるようになった
- さらに query-only incremental watch 中で、cheap 差分または DB length 未確定の時だけ metadata probe を許し、watch existing movie の `MovieLength` 変更も局所更新へ流せるようになった
- そのうえで `{dup}` 検索だけは `Hash` 変化時に changed-path 局所更新を使わず、full in-memory filter へ戻す安全弁を入れた
- そのうえで通常検索では `MovieSize / FileDate / MovieLength` など検索非依存 dirty の時、changed path ごとの `FilterMovies(...)` 呼び出しも省くようにした

### 7.2 Phase 4 の次の着手順

1. `CheckFolderAsync` に残る `visible-only gate / zero-byte / first-hit 通知 / final queue flush` を、テンポを落とさない範囲でさらに coordinator 化する
2. watch event DTO と queue 処理を `MainWindow` 依存からさらに離し、`WatcherEventDispatcher` 相当へ寄せる
3. watch 起点の UI 再読込を、差分反映優先でさらに縮小できる箇所を切り分ける
  現在は `changed paths + ChangeKind + DirtyFields + ObservedState` ベースの局所 filter / 直接復帰 / rename reuse-order / existing movie file属性反映 / query-only incremental watch時の必要時限定probe / `{dup}` 時の安全fallback まで。次は `Hash` を safe に局所反映できる条件を見極め、watch existing movie の局所 sort 回避条件をさらに広げる

### 7.2.1 Phase 4 の抜本方針

- `Views/Main/MainWindow.xaml.cs` の `FilterAndSortAsync(...)` を中心に残っている「少数変更でも全面再評価へ戻る構造」を崩し、一覧 UI を差分反映中心へ寄せる。
- watch、画像供給、起動、skin 切り替えは個別最適でなく、この軸に沿って進める。
- 詳細は `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md` を正本とする。

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

- repair が走った条件
- repair が走らなかった条件
- `No frames decoded` で救えた条件
- `No frames decoded` でも救えなかった条件
- `ERROR` マーカー固定へ落ちた条件

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
