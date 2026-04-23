# Implementation Plan: skin切り替え高速化 DB保存分離先行 2026-04-13

最終更新日: 2026-04-22

変更概要:
- 全体プラン見直しを受けて、`skin` 切り替え高速化の中で DB を「先頭の決定打」ではなく「第2群の土台施策」として位置づけ直した
- `refresh` 起点一本化、stale 判定前倒し、catalog 再走査削減を DB より先に進める順序へ組み替えた
- DB write は `WhiteBrowserSkinOrchestrator` と外部 skin API を最初から同一 persister に統合する方針へ改めた
- session cache は enqueue 時に正本化せず、persist 成功反映か dirty / fault 管理を前提にする方針へ改めた
- shutdown は `writer complete -> bounded drain -> timeout 時だけ cancel` を原則にする形へ改めた
- `DebugRuntimeLog` に async flow scope を追加し、`skin-webview` で作った trace を `skin-catalog` / `skin-db` まで同じ行頭文脈で追えるようにした
- `skin-webview` には `refresh begin / refresh end` の要約ログも追加し、切り替え 1 回ぶんを `trace` 単位で前から読めるようにした
- `refresh end` には `elapsed_ms` も追加し、切り替え 1 回あたりの体感コストを `trace` 単位で読み返せるようにした
- `WhiteBrowserSkinCatalogService` は nonstandard な html 名でも前回 `HtmlPath` を優先再利用し、fallback の `EnumerateFiles` を毎回踏まない形へ寄せた
- `WhiteBrowserSkinStatePersistRequest` に trace を持たせ、queue をまたぐ `skin-db` の persister ログでも `trace=rqXXXX` を維持できるようにした
- `refresh end` には ambient counter 由来の `catalog_hit / catalog_miss / persist_enqueued / persist_fallback_applied` 要約も出せるようにし、1 回の切り替えで何が起きたかを end 1 行で見返せるようにした
- `refresh end` の ambient 要約は `catalog_reused / catalog_skipped / catalog_signature_ms / catalog_load_ms` まで読めるようにし、cache miss 時の再利用量と catalog 側コストも 1 行で追えるようにした。`catalog_*_ms` は trace 内の合計値として扱う
- `refresh end` には `skinResolved` と短い `dbKey` も載せ、フルパスを追わなくても「どの skin / どの DB の切り替えか」が end 1 行で分かるようにした
- `refresh end` には `outcome=applied / fallback / standard` も載せ、件数や時間だけでなく「結局どう終わったか」も短く読み返せるようにした
- `WhiteBrowserSkinCatalogService` は、built-in 同名 external フォルダを snapshot 入口で除外するようにし、結果へ絶対採用しない skin の `HtmlPath` 解決や metadata 確認を減らした
- `WhiteBrowserSkinCatalogService` の fallback HTML 解決は 1 回の列挙へまとめ、`.htm` 優先を保ったまま `EnumerateFiles("*.htm")` / `EnumerateFiles("*.html")` の二重走査を避けるようにした
- `WhiteBrowserSkinCatalogService.ResolveSkinHtmlPath(...)` は、標準名優先・前回 custom HTML 維持・fallback `.htm` 優先を 1 回の directory 列挙で同時に決める形へ寄せた。標準名追加時の乗り換えと custom HTML 維持の両方を focused test で固定した
- `BuildCatalogSnapshot(...)` は html path 解決と metadata 取得を同じ helper で返す形へ寄せ、`Resolve -> File.Exists -> FileInfo` の往復を減らした。directory 時刻は補助に留め、最終判定は html 実ファイル metadata を使う既存意味論を維持している
- skin 本線の終盤計画として、`TagInputRelation / umiFindTreeEve` の MainWindow 実 host と runtime bridge 実 host の terminal 再描画・`changeSkin` 境界・dirty state 残差を、完了条件ベースで詰める順序を追記した
- `TagInputRelation` の bare terminal rerender (`onClearAll/onSkinLeave -> onExtensionUpdated`) は、MainWindow 実 host 側も focused 2 件通過で固定し、両実 host の固定済み領域へ昇格した
- `TagInputRelation` の bare terminal success (`onClearAll/onSkinLeave -> changeSkin("#umlFindTreeEve")`) は、MainWindow 実 host 側も focused 4 件通過で固定し、両実 host の固定済み領域へ昇格した

## 1. 結論

今回まず入れるべきなのは、`skin` 切り替え時の **無駄な refresh 仕事を減らすこと** である。

DB 分離は依然として必要だが、着手順は主因の後ろへ下げる。

本計画での位置づけは次である。

1. `refresh` 起点を 1 本化し、二重 queue を止める
2. stale 判定を host 準備より前へ寄せる
3. `WhiteBrowserSkinCatalogService.Load(...)` の常時再走査を止める
4. その後で保存系 DB write を UI スレッドから外す
5. 外部 skin API の profile 読み書きも UI 状態取得と DB 実行を分離する
6. `SelectProfileValue(...)` の最適化は整合条件を固めてから最後に進める

つまり、**DB 分離は必要だが、体感改善の主因ではない** というのが採用方針である。

## 2. 目的

- `skin` 切り替え時の同期 SQLite I/O を UI 直列経路から外し、体感の引っかかりを減らす
- 外部 skin API からの profile 読み書きでも、UI スレッド滞在時間を最小化する
- 将来の `skin` 切り替え高速化で、DB 由来の負荷がボトルネックとして残らない土台を作る

## 3. 対象範囲

今回の計画対象は次に限定する。

- `Views/Main/MainWindow.WebViewSkin.cs`
- `Views/Main/ExternalSkinHostRefreshScheduler.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinCatalogService.cs`
- `WhiteBrowserSkin/MainWindow.Skin.cs`
- `Views/Main/MainWindow.WebViewSkin.Api.cs`
- `DB/SQLite.cs`
- `Views/Main/MainWindow.xaml.cs` もしくは `WhiteBrowserSkin` 配下の新規 persister 関連クラス
- `Tests/IndigoMovieManager.Tests` の skin 切り替え / API / persister 系テスト

今回やらないことは次である。

- `WebView2` 実体の再設計や全面的な document 差し替え中心化
- `built-in skin` 側 decode / 詳細ペイン最適化
- `SQLite` 全 API の一括 async 化

## 3.1 検証用 worktree / 退避コピーの運用ルール

- skin 検証では detached worktree や退避コピーを使ってよいが、リポジトリ直下に `HEAD` や commit hash 名フォルダを作らない。
- 検証用 worktree は `C:\Users\na6ce\source\repos\IndigoMovieManager-worktree-*` のような sibling ディレクトリへ作る。
- 検証用 worktree や退避コピーは、green 確認後に必ず削除する。
- `IndigoMovieManager.csproj` の除外設定は保険として維持してよいが、再発防止の正本は「repo 直下へ残置しない運用」である。

## 4. 現状再確認

### 4.1 skin 切り替え本線の同期 DB 呼び出し

- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:103`
  - `ApplySkinByName(...)` 成功時に `PersistCurrentSkinState(...)`
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:134`
  - `UpsertSystemTable(...)`
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:135`
  - `UpsertProfileTable(...)`
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs:218`
  - `SelectProfileValue(...)`

### 4.2 外部 skin API 側の UI スレッド寄り DB 呼び出し

- `Views/Main/MainWindow.WebViewSkin.Api.cs:507`
  - `GetExternalSkinProfileValueAsync(...)`
- `Views/Main/MainWindow.WebViewSkin.Api.cs:535`
  - `WriteExternalSkinProfileValueAsync(...)`
- `Views/Main/MainWindow.WebViewSkin.Api.cs:797`
  - `InvokeExternalSkinUiActionAsync(...)`
- `Views/Main/MainWindow.WebViewSkin.Api.cs:819`
  - `InvokeExternalSkinUiTaskAsync(...)`

### 4.3 分離しやすい理由

- `DB/SQLite.cs:444`
  - `CreateReadOnlyConnection(...)`
- `DB/SQLite.cs:450`
  - `CreateReadWriteConnection(...)`

`SQLiteConnection` は共有されず、各メソッドで毎回 open / close している。
そのため、呼び出し側の責務整理だけで別スレッド化しやすい。

## 5. 採用方針

### 5.1 主因の削減を先に行う

全体高速化としては、先に次を進める。

1. `refresh` 起点の一本化
2. stale 判定の前倒し
3. `WhiteBrowserSkinCatalogService.Load(...)` の再走査削減

この 3 点は、現状の支配要因に直接効く。
DB はその後で「UI 経路に残る同期仕事を減らす土台」として入れる。

### 5.2 保存系 write は単一ライターの非同期 persister へ寄せる

`system.skin` と `profile.LastUpperTab` の保存、および外部 skin API 経由の profile write は、UI スレッドから直接 `SQLite` を叩かず、**最初から同一の単一ライター persister** へ渡す。

基本構造は次とする。

1. UI 側は保存要求 DTO を組み立てる
2. `Channel<...>` へ即時投入する
3. 背景 persister が順番に `UpsertSystemTable(...)` / `UpsertProfileTable(...)` を実行する

この方式は、既存の `ThumbnailQueuePersister` 系と同じ思想であり、プロジェクト内の実績ある構造へ寄せられる。

### 5.3 読み取りは一気に async 化しない

`SelectProfileValue(...)` は「初期タブ復元」の前段にあるため、単純な fire-and-forget 化は採らない。

先に行うのは次である。

1. 保存系 write の統一と UI 経路外し
2. 必要なら session cache を導入する
3. cache miss 時だけ同期 DB 読み取りする

ただし session cache は enqueue 時に正本化しない。
persist 成功反映か、少なくとも dirty / fault 状態を区別できる形で扱う。

### 5.4 外部 skin API は UI 状態取得と DB 実行を分離する

`GetExternalSkinProfileValueAsync(...)` と `WriteExternalSkinProfileValueAsync(...)` は、現在 UI 状態取得と DB 呼び出しが同じ delegate に混ざっている。

ここは次の二段へ分ける。

1. UI スレッドで `dbFullPath` / `skinName` / `key` / `value` だけ取得する
2. write は persister へ渡す
3. read は UI スレッド外で行う

これにより、UI スレッドに残る仕事を最小化できる。

### 5.5 shutdown は drain を先に行う

終了時は次を原則にする。

1. 新規 enqueue を止める
2. writer を complete する
3. bounded drain を短時間だけ待つ
4. timeout 時だけ `CancellationToken` で停止を促す

これにより、「最後の状態を残したい」のに cancel が先に効いて drain を潰す事態を避ける。

### 5.6 失敗時は UI を止めずログへ寄せる

現行 `SQLite` 実装は DB エラー時にログへ寄せる構造を持っている。
本計画でも、persister 側例外は UI へ再送出せず、`DebugRuntimeLog` へ残して supervisor で継続する。

## 6. 実装設計

### 6.1 新規コンポーネント

新規追加候補は次である。

1. `WhiteBrowserSkinStatePersistRequest`
   - `DbFullPath`
   - `PersistedSkinName`
   - `ProfileSkinName`
   - `LastUpperTabStateName`
   - `RequiresProfileWrite`
2. `WhiteBrowserSkinStatePersister`
   - `ChannelReader<WhiteBrowserSkinStatePersistRequest>` を読む
   - `UpsertSystemTable(...)` / `UpsertProfileTable(...)` を実行する
3. `WhiteBrowserSkinProfileSessionCache`
   - `dbFullPath + skinName + key` 単位で in-memory 保持する

命名は実装時に微調整してよいが、責務はこの 3 分割を維持する。

### 6.2 依存の流れ

依存方向は次に固定する。

1. `MainWindow`
   - `refresh` 起点の制御
   - channel
   - persister
   - supervisor task
   - shutdown 制御
2. `WhiteBrowserSkinOrchestrator`
   - 保存要求を直接 DB へ書かず、enqueue delegate を呼ぶ
   - tab 復元は cache -> DB fallback
3. `MainWindow.WebViewSkin` / `ExternalSkinHostRefreshScheduler`
   - refresh 起点一本化
   - stale 判定前倒し
   - catalog 利用経路整理
4. `MainWindow.WebViewSkin.Api`
   - UI 状態の snapshot 取得だけ UI スレッド
   - write は persister 経由
   - read は background 実行

これにより、orchestrator 自身は「永続化の具体実装」を持たずに済む。

### 6.3 dedupe 方針

保存要求は連続で同じ値が積まれやすいので、persister では次の最小 dedupe を行う。

- 同じ `dbFullPath` に対する連続要求では、最後の `system.skin` を優先する
- 同じ `dbFullPath + profileSkinName + key` に対する連続要求では、最後の `value` を優先する

最初は「バッチ内の最後勝ち」で十分とする。
高度な圧縮は後続フェーズでよい。

### 6.4 shutdown 方針

終了時は次を行う。

1. 新規 enqueue を止める
2. writer を complete する
3. persister の drain を短時間だけ待つ
4. timeout 時だけ `CancellationToken` を通知する

待機上限は、既存の background task と揃えて **最大 500ms 前後** を目安にする。

## 7. Phase 分割

## Phase 0: 計測と足場

### 7.1 目的

主因と土台施策を分けて、前後比較できるようにする。

### 7.2 作業

- `QueueExternalSkinHostRefresh(...)` と refresh 完了までの経過時間ログ
- stale 破棄件数のログ
- `WhiteBrowserSkinCatalogService.Load(...)` の経過時間ログ
- `PersistCurrentSkinState(...)` の経過時間ログ
- `SelectProfileValue(...)` の経過時間ログ
- API profile 読み書きの経過時間ログ
- persister enqueue 数 / 実書き込み数 / dedupe 数のログ

### 7.3 完了条件

- `debug-runtime.log` だけで `refresh` / catalog / DB のどこが支配的か追える

## Phase 1: refresh 起点一本化と stale 判定前倒し

### 7.4 目的

外部 skin 切り替え 1 回で、無駄な prepare / navigate をなるべく始めないようにする。

### 7.5 作業

- `DbInfo.Skin` の `PropertyChanged` 経由と `ApplySkinByName(...)` 明示 queue のどちらを正本にするか決める
- `QueueExternalSkinHostRefresh(...)` 起点を 1 本化する
- `RefreshExternalSkinHostPresentationAsync(...)` の前段で generation を再確認し、古い要求の host 準備を抑止する

2026-04-14 進捗:

- `ApplySkinByName(...)` からの明示 queue を外し、`DbInfo.Skin` の変化を refresh 正本へ寄せた
- stale 判定を refresh 開始直後、definition 解決後、prepare 中、apply 前へ追加した
- 2026-04-15: MainWindow 実 host 統合テストで、`ApplySkinByName(...)` 経由の外部 skin 切替は `dbinfo-Skin` を refresh 起点として 1 回だけ apply されることを確認した
- 2026-04-15: `BootNewDb(...)` 中の `DBFullPath / Skin / ThumbFolder` 変化は batch 化し、外部 skin refresh は最後に `dbinfo-DBFullPath` へ 1 回だけ流す形へ整理した。旧 `boot-new-db` の特例 reason は外し、MainWindow 実 host 統合テストで `Prepare == 1`、`apply == 1` を確認した
- 2026-04-15: `MainWindow_ContentRendered -> TrySwitchMainDb(...) -> BootNewDb(...)` の起動復元経路でも、外部 skin refresh は `dbinfo-DBFullPath` に収束することを MainWindow 実 host 統合テストで確認した。起動復元だけ別 reason へ逃げる状態ではなくなった
- 2026-04-15: `ThumbFolder` 変更は外部 skin host に渡すサムネ root 実体が変わるため、`dbinfo-ThumbFolder` は独立 refresh 起点として維持する。MainWindow 実 host 統合テストで、外部 skin 表示中の `ThumbFolder` 変更は `Prepare == 1` 追加、reason は `dbinfo-ThumbFolder` で 1 回だけ再準備されることを確認した
- 2026-04-15: `MainWindow.WebViewSkin` の batch begin / flush も `skin-webview` ログへ残すようにし、flush 時は `preferred` / `batched` / `skinRaw` / `db` まで残すようにした。`refresh deferred` と `catalog cache hit/miss` を `debug-runtime.log` 上で時系列に追いやすくした
- 2026-04-16: `MainWindow.SkinPersistence` では `persist queued` と `system/persist fallback applied` も `skin-db` ログへ残すようにした。`refresh / catalog / persist` を同じ `debug-runtime.log` で前から順に追いやすくした

### 7.6 完了条件

- 外部 skin 切り替え 1 回で refresh が実質 1 回へ近づく
- stale な prepare / navigate が後段まで進みにくくなる

## Phase 2: catalog 再走査削減

### 7.7 目的

skin 名解決や minimal chrome 同期のたびに、catalog を常時総なめしないようにする。

### 7.8 作業

- `WhiteBrowserSkinCatalogService.Load(...)` の cache を導入する
- `GetAvailableSkinDefinitions()` と definition 解決が同じ cache を参照するように整理する
- minimal chrome の skin ドロップダウンも同じ cache を使う

2026-04-15 進捗:

- `WhiteBrowserSkinCatalogService.Load(...)` に cache hit / miss の runtime log を追加した
- `same root` 再読込では `hit` だけ増え、html 更新後だけ `miss` が増えることを focused test で確認できるようにした
- `WhiteBrowserSkinCatalogService.BuildCatalogSignature(...)` も `directories` と `elapsed_ms` を `skin-catalog` ログへ残すようにし、cache 判定より前に掛かる署名計算コストも見えるようにした
- `BuildCatalogSignature(...)` 相当の snapshot 作成も、前回 cache と一致するディレクトリは html metadata を再利用するようにした。focused test では `same root` 再読込で `reused` が増え、一部更新時は未変更ディレクトリだけ再利用される
- miss 時は signature 用 metadata と実 load を同じ snapshot で共有する形へ寄せ、catalog 再読込時のディレクトリ総なめを 2 回繰り返さないようにした
- `LoadCore(...)` も `catalog load core built` として `items` / `external` / `elapsed_ms` を `skin-catalog` ログへ残すようにし、署名計算と実定義生成のどちらが重いかも分けて見えるようにした
- `catalog load core built` にも `root` を出すようにし、`signature built / cache hit / miss / load core built` を同じ skin root 軸で読み合わせやすくした
- `LoadCore(...)` は miss 時でも、前回 snapshot と一致する外部 skin については `WhiteBrowserSkinDefinition` を参照再利用するようにした。focused test では 2 件中 1 件だけ更新した再読込で、未変更側は同一参照、更新側だけ差し替わり、`reused` 件数 telemetry も取得できる
- `LoadCore(...)` の定義再利用判定は html metadata 基準へ寄せ、CSS / JS / 画像など非 HTML 資産だけ更新した時も `WhiteBrowserSkinDefinition` を参照再利用できるようにした。focused test で「asset 更新だけなら miss でも definition は再利用される」ことを固定した
- `WhiteBrowserSkinOrchestrator` の snapshot 構築は loaded definitions ベースへ寄せ、`GetAvailableSkinDefinitions() -> ApplySkinByName(...) -> GetAvailableSkinDefinitions()` の余分な catalog hit を減らした
- `WhiteBrowserSkinOrchestrator` 経由でも、一覧再取得時に未変更 skin 定義が参照再利用されることを focused test で確認した。MainWindow 相当の利用経路でも、html を触っていない skin まで毎回作り直さない
- `MainWindow.WebViewSkin` の batch begin / flush ログと合わせ、`skin-webview` と `skin-catalog` を同じ `debug-runtime.log` だけで並べて追える状態にした
- `skin-webview` の `refresh deferred / queued / batch begin / batch flush` には `batch=btXXXX` と `request=rqXXXX` の短い識別子も載せ、同じ切替単位の流れを 1 本で追いやすくした
- `request=rqXXXX` は `host prepare begin` / `host navigate failed` / `refresh skipped stale` / `host presentation` にも引き継ぎ、queue された refresh が apply 完了までどう流れたかを追いやすくした
- `MainWindow.SkinPersistence` の `persist queued` / `fallback applied` と合わせ、`skin-db` も同じ `debug-runtime.log` で読み合わせできるようにした
- `debug-runtime.log` の全カテゴリ行へ共通連番を付け、`skin-webview / skin-catalog / skin-db` を時系列で追い返しやすくした
- `debug-runtime.log` の1行性を守るため、カテゴリ名とメッセージ中の改行・タブは空白化するようにした

2026-04-14 進捗:

- `WhiteBrowserSkinCatalogService.Load(...)` に root 単位 cache を追加した
- skin ディレクトリ名と html 更新時刻を含む signature で cache 無効化できるようにした
- catalog cache の再利用と html 更新時の再読込を単体テストで確認した
- signature build 回数、最後に走った directory 数、経過時間 telemetry も focused test から取得できるようにした
- load core 回数、最後に生成した external skin 数、経過時間 telemetry も focused test から取得できるようにした

### 7.9 完了条件

- catalog 再走査が切り替えのたびに常時発生しない
- 一覧取得と definition 解決で重複した再走査が減る

## Phase 3: 保存系 DB I/O を単一ライターへ統合する

### 7.10 目的

`ApplySkinByName(...)` と外部 skin API write に残る同期 DB write を UI スレッドから外す。

### 7.11 作業

- `Channel<WhiteBrowserSkinStatePersistRequest>` を導入する
- `WhiteBrowserSkinStatePersister` を追加する
- `MainWindow` 起動時に supervisor を開始する
- `MainWindow` 終了時に `writer complete -> drain -> timeout 時だけ cancel` を追加する
- `WhiteBrowserSkinOrchestrator.PersistCurrentSkinState(...)` は request 構築 + enqueue のみ行う
- `WriteExternalSkinProfileValueAsync(...)` も同じ persister へ統合する

2026-04-14 進捗:

- `WhiteBrowserSkinStatePersistRequest` / `WhiteBrowserSkinStatePersister` を追加した
- `PersistCurrentSkinState(...)` は `system.skin` / `profile.LastUpperTab` の request enqueue のみ行うよう変更した
- 外部 skin API の profile write も同じ persister へ合流した
- `MainWindow` 起点の `sort` / 個別設定 (`thum` / `bookmark` / `keepHistory` / `playerPrg` / `playerParam`) も同じ persister 優先へ寄せた
- `Watcher` の `last_sync` 保存も同じ persister 優先へ寄せ、通常運用の `system` 直書きをさらに縮小した
- 設定保存直後の `GetSystemTable(...)` 再読込は外し、runtime の `systemData` / `MainVM.DbInfo` を先に揃える形へ修正した
- shutdown は `writer complete -> 500ms drain -> timeout 時だけ cancel` の順へ変更した
- shutdown 開始後の background 側 `system` 直書きは減らし、`Everything` poll 停止を writer completion より先へ寄せた
- 2026-04-15: `MainWindow` の `UpdateSort()` について、通常時は persister 経由で `system.sort` へ保存できること、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は queue 拒否を検知して fallback 直書きへ戻せることを MainWindow 受け入れテストで確認した
- 2026-04-15: `MainWindow` の `UpdateSkin()` について、通常時は `system.skin` と外部 skin の `profile.LastUpperTab` を persister 経由で保存できること、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は queue 拒否を検知して両方とも fallback 直書きへ戻せることを MainWindow 受け入れテストで確認した
- 2026-04-15: `MainWindow` の `ApplySkinByName("DefaultGrid", persistToCurrentDb: true)` についても、通常時は built-in skin の `system.skin` を persister 経由で保存でき、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は queue 拒否を検知して fallback 直書きへ戻せることを MainWindow 受け入れテストで確認した
- 2026-04-15: `Watcher` の `SaveEverythingLastSyncUtc(...)` についても、通常時は persister 経由で `system.everything_last_sync_utc_*` へ保存できること、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は queue 拒否を検知して fallback 直書きへ戻せることを MainWindow 受け入れテストで確認した
- 2026-04-15: `MenuBtnSettings_Click` の個別設定保存は `PersistDbSettingsValues(...)` へ集約し、`thum` / `bookmark` / `keepHistory` / `playerPrg` / `playerParam` の通常保存と `BeginWhiteBrowserSkinStatePersisterShutdown()` 後 fallback を MainWindow 受け入れテストで確認した

### 7.12 完了条件

- `ApplySkinByName(...)` から `UpsertSystemTable(...)` / `UpsertProfileTable(...)` の同期呼び出しが消える
- API profile write も単一ライター経路へ統合される

## Phase 4: API profile read と初期タブ復元の整合改善

### 7.13 目的

profile read の UI スレッド滞在を短くしつつ、初期表示整合を崩さない。

### 7.14 作業

- `GetExternalSkinProfileValueAsync(...)` で UI snapshot を先に取る
- `SelectProfileValue(...)` は background 実行へ移す
- 必要なら `LastUpperTab` の session cache を導入する
- cache を入れる場合は persist 成功反映か dirty / fault 管理を前提にする
- `ResolveInitialTabStateNameForSkin(...)` は cache と DB fallback の整合を保つ

2026-04-14 進捗:

- 外部 skin API の `getProfile` は UI で `dbFullPath / skinName / key` の snapshot 取得だけを行い、`SelectProfileValue(...)` 自体は background 実行へ移した
- 初期タブ復元に使う `ResolveInitialTabStateNameForSkin(...)` はまだ同期のままとし、表示整合を崩さない段階で止めている
- `WhiteBrowserSkinProfileValueCache` を追加し、`pending / persisted / faulted` を分けるようにした
- API の `getProfile` は `pending` も見えるが、初期タブ復元は `persisted` だけを見る形にした

### 7.15 完了条件

- API profile 読み取りで `Dispatcher.Invoke` / `InvokeAsync` 内に `SQLite` 呼び出しが残らない
- 同一セッション中の `LastUpperTab` 復元で、無駄な DB read を減らしつつ保存失敗を隠さない

## 8. テスト方針

### 8.1 単体テスト

- refresh 起点一本化後に不要な queue が増えない
- stale 判定前倒しで古い要求の重い準備が抑止される
- catalog cache が同一プロセス内で再利用される
- persister が request を正しく `UpsertSystemTable` / `UpsertProfileTable` へ流す
- API profile write が同じ persister へ流れる
- 同一キーの連続 request で最後勝ちになる
- shutdown 時に drain 優先で短時間停止する
- session cache を入れる場合は `dbFullPath + skin + key` 単位で分離され、失敗状態を隠さない

### 8.2 統合テスト

- 外部 skin 切り替え 1 回で refresh が実質 1 回へ近づく
- catalog 再走査が切り替えのたびに常時発生しない
- `ApplySkinByName(...)` 後に DB 保存結果が反映される
- 外部 skin API の profile 読み書きが従来どおり成功する
- DB 保存失敗時も UI 側の切り替えは継続する

### 8.3 実機確認

- 外部 skin 切り替えを連打しても UI の引っかかりが悪化しない
- 連打時に stale な prepare / navigate が大きく減る
- UNC / ネットワーク遅延環境でも保存由来の詰まりが減る
- 終了直前の skin 変更でも、最後の状態が大きく取りこぼれない

## 9. リスク

### 9.1 保存取りこぼし

終了直前に要求が積まれた場合、persister drain 前にプロセス終了すると最後の 1 件を落とす可能性がある。

対策:

- `writer complete -> drain -> timeout 時だけ cancel`
- 重要 request の最後勝ち圧縮
- 失敗時ログ

### 9.2 初期タブ復元との整合

保存だけ async 化すると、直後の再読込タイミングによっては古い `LastUpperTab` が見える可能性がある。

対策:

- session cache を enqueue 時に正本化しない
- persist 成功反映か dirty / fault 管理を前提にする
- 同一セッション内でも失敗状態を隠さない

### 9.3 直列 worker の停止

persister が死ぬと保存が止まる。

対策:

- supervisor で再起動する
- `queue-db` と同様に再起動ログを残す

### 9.4 主因に先に手を付けないまま DB だけ進めるリスク

DB 分離だけを先に進めると、`refresh` 二重化や catalog 再走査が残ったままで体感差が薄い可能性がある。

対策:

- Phase 1 と Phase 2 を先に進める
- 計測ログで `refresh` / catalog / DB の内訳を比較する
- 受け入れ条件を DB 単独ではなく全体テンポで判定する

## 10. 受け入れ条件

1. 外部 skin 切り替え 1 回で refresh が実質 1 回へ近づいている
2. catalog 再走査が常時発生しない
3. `ApplySkinByName(...)` と API profile write の DB write が UI スレッド同期 I/O になっていない
4. 同一セッション中の `LastUpperTab` 復元で無駄な DB read を減らしつつ、保存失敗を隠していない
5. 既存の skin 切り替え表示互換を壊していない
6. ログで `refresh` / catalog / enqueue / persist / fail / restart を追跡できる

## 11. この計画の次

本計画完了後の優先順位は次とする。

1. 外部 skin 同士の document 差し替え中心化
2. built-in skin 側 decode / 詳細ペイン最適化
3. `SelectProfileValue(...)` を含む profile 読み取り最適化の再評価

DB 分離は、その後の高速化施策を素直に効かせるための土台と位置づける。

## 12. 2026-04-20 時点の次の実施順

skin 本線はかなり高進捗まで来ているため、ここからは「未実装を増やす」より「残差を対称に潰す」ことを優先する。

### 12.1 最優先

- `TagInputRelation / umiFindTreeEve` の MainWindow 実 host で、terminal callback 後の再描画と dirty state 終端の見た目残差を固定する
- runtime bridge で green になっていて MainWindow 側が薄いケースだけを追加し、二重管理を避けながら対称性をそろえる

### 12.2 次点

- build 出力 skin 4 本
  - `DefaultSmallWB`
  - `Chappy`
  - `Search_table`
  - `Alpha2`
- この 4 本について、`tag / thumb` の差分更新後に `terminal rerender / changeSkin success / changeSkin failure` が揃っているかを MainWindow 実 host で棚卸しする
- runtime bridge 正本だけで十分に言い切れているケースは、MainWindow 側へ無理に複製しない

### 12.3 ドキュメント整理

- `WhiteBrowserSkin/Docs/Progress_SkinFeature_2026-04-07.md`
- `WhiteBrowserSkin/Docs/未表示外部スキン攻略メモ_2026-04-12.md`

この 2 本は、次の 3 区分で読める状態へ保つ。

- 完了済み
- 実測どおり非対称で固定済み
- まだ MainWindow / runtime bridge のどちらかだけが薄い

2026-04-20 時点では、特に次を「残差あり」として明示して扱う。

- `TagInputRelation` の MainWindow 実 host における `Get後 -> onSkinLeave/onClearAll -> changeSkin("MissingSkin") -> changeSkin("#umlFindTreeEve")`
  - runtime bridge 側は green だが、MainWindow 側は `MS.Win32.HwndSubclass.SubclassWndProc` 起点の fail-fast が混ざるため、まだ正本化しない
- `umiFindTreeEve` の MainWindow 実 host における `onModifyTags -> Refresh() -> onSkinLeave/onClearAll -> changeSkin("#TagInputRelation")`
  - runtime bridge 側は green だが、MainWindow 側は focused 実行の teardown で `MS.Win32.HwndSubclass.SubclassWndProc` 起点の fail-fast が混ざったため、まだ正本化しない
- build 出力 skin 4 本の MainWindow 実 host における `onUpdateThum -> onSkinLeave/onClearAll -> Refresh()`
  - runtime bridge 側は green だが、MainWindow 側は focused 実行の teardown で `MS.Win32.HwndSubclass.SubclassWndProc` 起点の fail-fast が混ざったため、まだ正本化しない
- `TagInputRelation` の runtime bridge 実 host における `Save後 -> onSkinLeave/onClearAll -> changeSkin("MissingSkin") -> changeSkin("#umlFindTreeEve")`
  - `failure 単体` と `success 単体` は green だが、直列では最初の `MissingSkin` 結果待ち自体が安定せず、まだ正本化しない
- build 出力 skin 4 本の runtime bridge における `tag差分更新後 -> terminal -> changeSkin("MissingSkin") -> changeSkin(nextSkin)`
  - `failure 単体` と `success 単体` は green だが、直列では 2 回目の `changeSkin` 完了待ちが timeout する
  - いまは無理に押し込まず、専用調査対象として分離する
- `umiFindTreeEve` の runtime bridge 実 host における `onSkinLeave -> changeSkin("MissingSkin") -> changeSkin("#TagInputRelation")`
  - `onSkinLeave -> MissingSkin(false)` の failure 単体と、`onSkinLeave -> TagInputRelation` の success 単体は green だが、直列では最初の `MissingSkin` 結果待ちが timeout するため、まだ正本化しない

### 12.4 再発防止

- 検証用 worktree や退避コピーは repo 直下へ置かない
- 検証用 worktree は sibling ディレクトリへ作る
- 使用後は必ず削除し、`git status --short` で本体へ残置していないことを確認する

### 12.5 完了条件

- `TagInputRelation / umiFindTreeEve` の terminal / rerender / `changeSkin` 境界が MainWindow 実 host と runtime bridge 実 host の両方で説明できる
- build 出力 skin 4 本の `tag / thumb` 差分更新後境界が、どこまで MainWindow 正本で必要か判断済みである
- docs だけ読めば、完了済み・非対称固定済み・残差ありが区別できる

ただし「説明できる」は、すべてを対称に green 化する意味ではない。実測で非対称なら、その非対称を docs と test で正本化し、危険な直列遷移は未固定として分離できていることも完了条件に含める。

## 13. 2026-04-23 時点の固定状況インデックス

補足:
- この章はシナリオ名で整理している。実テスト名は日本語長名のため、1対1で完全一致しない場合がある。
- `未固定` は「該当シナリオのテストメソッドが無い」意味ではなく、「テストはあっても focused 実行で fail-fast / timeout が混ざり、固定済みへ昇格していない」意味で扱う。

### 13.1 両実 host で固定済み

- build 出力 skin 4 本 (`Search_table / Chappy / DefaultSmallWB / Alpha2`) の `tag / thumb` について、差分更新、`changeSkin success / failure`、`terminal + changeSkin success / failure` は `MainWindow` 実 host / `runtime bridge` 実 host の両方で正本化済み
- build 出力 skin 4 本 (`Search_table / Chappy / DefaultSmallWB / Alpha2`) の `tag / thumb` について、`runtime bridge` 実 host では `onClearAll/onSkinLeave -> 再入` による terminal rerender も正本化済み
- `TagInputRelation` の bare terminal rerender (`onClearAll/onSkinLeave -> onExtensionUpdated`) は `MainWindow` 実 host / `runtime bridge` 実 host の両方で正本化済み
- `TagInputRelation` の bare terminal failure (`onClearAll/onSkinLeave -> MissingSkin`) は `MainWindow` 実 host / `runtime bridge` 実 host の両方で正本化済み
- `TagInputRelation` の bare terminal success (`onClearAll/onSkinLeave -> changeSkin("#umlFindTreeEve")`) は `MainWindow` 実 host / `runtime bridge` 実 host の両方で正本化済み
- `umiFindTreeEve` の `clear/leave -> Refresh` は `register / tag / path / remove` の 4 系統で `MainWindow` 実 host / `runtime bridge` 実 host の両方で正本化済み

### 13.2 MainWindow 側が未固定

- `TagInputRelation` の MainWindow 実 host における `Get後 -> terminal -> MissingSkin -> success` 直列は、WPF fail-fast が混ざるため未固定
- `umiFindTreeEve` の MainWindow 実 host における bare な `onClearAll/onSkinLeave -> MissingSkin -> changeSkin("#TagInputRelation")` も、近接 2 件は通るが focused 実行終了時に `MS.Win32.HwndSubclass.SubclassWndProc` 起点の host crash が混ざったため、bare terminal の failure -> success 直列も未固定
- `umiFindTreeEve` の MainWindow 実 host における `onModifyTags -> Refresh() -> terminal -> changeSkin("#TagInputRelation")` は、新規ケース自体は通るが focused 束の teardown fail-fast が混ざるため未固定
- `umiFindTreeEve` の MainWindow 実 host における `onModifyTags -> onSkinLeave -> Refresh() -> changeSkin("#TagInputRelation")` は、さらに代表 1 ケースへ絞っても `MS.Win32.HwndSubclass.SubclassWndProc` 起点の host crash が混ざったため、束ね方ではなく MainWindow 側終了処理を含む未固定境界として扱う
- `umiFindTreeEve` の MainWindow 実 host における `onRegistedFile -> onClearAll/onSkinLeave -> changeSkin("#TagInputRelation")` も、新規 assertion 自体は通るが focused 実行終了時に `MS.Win32.HwndSubclass.SubclassWndProc` 起点の host crash が混ざったため、register 後 terminal success も未固定の success 境界として扱う
- build 出力 skin 4 本の MainWindow 実 host における `onUpdateThum -> terminal -> Refresh()` は、代表ケースでも初期 thumb への復帰をまだ正本化できていないため未固定
- 上記のうち `DefaultSmallWB + onClearAll -> Refresh()` を単独 1 ケースへ絞っても、focused 実行終了時に `MS.Win32.HwndSubclass.SubclassWndProc` 起点の host crash が再現したため、build 出力 skin の `thumb rerender` は bundle 依存ではなく MainWindow 側 teardown を含む未固定境界として扱う
- build 出力 skin 4 本の MainWindow 実 host における `onModifyTags -> terminal -> Refresh()` も、代表ケースで `fresh-tag` が消えず初期 tag 表示へ戻るところをまだ正本化できていないため未固定
- 上記のうち `DefaultSmallWB + onClearAll -> Refresh()` を単独 1 ケースへ絞っても、focused 実行終了時に `MS.Win32.HwndSubclass.SubclassWndProc` 起点の host crash が再現したため、build 出力 skin の `tag rerender` も bundle 依存ではなく MainWindow 側 teardown を含む未固定境界として扱う
- build 出力 skin 4 本の MainWindow 実 host における `onModifyTags -> onClearAll -> MissingSkin -> success` 直列も、`Search_table` 代表ケースでは新規 assertion 自体は通るが、focused 実行の終了時に `MS.Win32.HwndSubclass.SubclassWndProc` 起点の fail-fast が混ざったため、まだ未固定の serial success 境界として扱う

### 13.3 runtime bridge 側が未固定

- `TagInputRelation` の runtime bridge 直列は、2026-04-23 時点で次の切り分けを正本とする
  - bare terminal: `failure` 単体 / `success` 単体は green、`MissingSkin -> #umlFindTreeEve` は timeout
  - `Get後`: `terminal -> success` と `terminal -> failure` は green、`terminal -> MissingSkin -> success` は未固定
  - `Save後`: `terminal -> success` と `terminal -> failure` は green、`terminal -> MissingSkin -> success` は `Save後終端状態` 待機から揺れる

- `TagInputRelation` の runtime bridge 実 host における bare terminal (`onClearAll/onSkinLeave`) 後の `MissingSkin -> #umlFindTreeEve` 直列は、2 件とも `umlFindTreeEve` 側の完了待ちが timeout するため未固定
- 上記のうち `onClearAll -> MissingSkin -> #umlFindTreeEve` は、2026-04-22 に代表 1 ケースだけ再試行しても同じ timeout で再現したため、待機条件ではなく直列遷移そのものが未固定だと判断する
- `TagInputRelation` の runtime bridge 実 host における `Include/Save -> terminal -> MissingSkin -> success` は、最初の `MissingSkin` 結果待ちが安定せず未固定。2026-04-22 時点では `Save -> onClearAll/onSkinLeave -> MissingSkin -> #umlFindTreeEve` の単独 1 件へ絞っても、どちらも直列前提の `Save後終端状態` 待機から揺れが出る
- build 出力 skin 4 本の runtime bridge 実 host における `onUpdateThum -> onSkinLeave -> MissingSkin -> success` も、4 件とも 2 回目 `changeSkin` 完了待ちが timeout するため未固定
- build 出力 skin 4 本の runtime bridge 実 host における `onModifyTags -> terminal -> MissingSkin -> success` は、2 回目 `changeSkin` 完了待ちが timeout するため未固定
- 上記のうち `Search_table + onSkinLeave -> MissingSkin -> DefaultSmallWB` を代表 1 ケースだけ再試行しても、次 skin の tag baseline 復帰待ちが timeout したため、bundle 依存ではなく runtime bridge の serial timeout と判断する
- さらに `Search_table + onClearAll -> MissingSkin -> DefaultSmallWB` も、`tag` 側は 2 回目 `changeSkin` 以前の `MissingSkin` 結果待ちから timeout し、`thumb` 側も `terminal MissingSkin` 結果待ちから timeout したため、`clear` でも `leave` と同様に runtime bridge の serial timeout が再現する
- build 出力 skin 4 本の runtime bridge 直列は、2026-04-23 時点で `clear / leave` のどちらでも `MissingSkin -> success` が未固定とみなす
  - `tag`: `Search_table` 代表ケースで `leave` は次 skin の baseline 復帰待ち、`clear` は `MissingSkin` 結果待ちから timeout
  - `thumb`: `leave` は 2 回目 `changeSkin` 完了待ち、`clear` は `terminal MissingSkin` 結果待ちから timeout
- `umiFindTreeEve` の runtime bridge 実 host における `onSkinLeave/onClearAll -> MissingSkin -> #TagInputRelation` は、最初の `MissingSkin` 結果待ちが timeout するため未固定
