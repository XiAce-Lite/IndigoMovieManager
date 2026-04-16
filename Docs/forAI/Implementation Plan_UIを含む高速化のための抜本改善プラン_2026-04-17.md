# Implementation Plan UIを含む高速化のための抜本改善プラン 2026-04-17

最終更新日: 2026-04-17

変更概要:
- UI を含む高速化を、個別最適ではなく「全面再評価中心」から「差分反映中心」へ切り替える全体方針として整理
- `FilterAndSort`、watch 終端 reload、画像 I/O、skin 切り替え、起動導線を 1 本の計画で接続
- WhiteBrowser DB (`*.wb`) を変更せず、sidecar / cache / coordinator でテンポを上げる前提を明文化

## 1. 目的

- ユーザーが最初に触る一覧、検索、ページ移動、watch 反映、skin 切り替えの体感テンポを、局所改善ではなく構造変更で底上げする。
- 高速化の主戦場を「重い処理を少し速くする」から、「重い処理をそもそも起こさない」へ移す。
- 通常動画の初動を守りながら、rescue / queue / watcher / skin の既存本線と矛盾しない着手順を固定する。

## 2. いまの見立て

本丸は仮想化不足ではない。主因は、少数変更でも全面再評価へ戻る構造が複数箇所に残っていること。

### 2.1 一覧再評価

- `Views/Main/MainWindow.xaml.cs:1560` の `FilterAndSortAsync(...)` は、DB再読込、source 差し替え、全件 filter/sort、`Refresh()` までを 1 本で持つ。
- `Watcher/MainWindow.Watcher.cs:1193` と `Watcher/MainWindow.Watcher.cs:1225` では、watch 後の UI reload が最終的に `FilterAndSort(..., true)` へ戻る。
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
- warm start をさらに詰めるには、起動直後に必要な read model と、後で良い常駐処理をより明確に分ける必要がある。

## 3. 抜本方針

結論は 1 つ。

**一覧 UI を「全面再評価UI」から「差分反映UI」へ変える。**

この方針を成立させるため、以下の 5 本柱で進める。

1. Query state 分離
2. Diff-first 一覧反映
3. Watch からの差分通知化
4. Visible-first 画像供給の徹底
5. 起動・skin の warm path 短縮

## 4. 非機能の固定ルール

1. WhiteBrowser DB (`*.wb`) のスキーマは変更しない。
2. sidecar は補助であり正本にしない。壊れても fallback で戻れることを前提にする。
3. UI スレッドへ重い処理を戻さない。
4. 高速化のために観測性を削らない。
5. rescue / repair / queue の既定動作を重くしない。

## 5. 実施フェーズ

## Phase 0: 計測の固定

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

## Phase 1: Query state 分離

目的:
- `FilterAndSortAsync(...)` に集中している責務を分解し、毎回全件を触る構造を崩す。

実施内容:
- `MainWindow` 直下にある検索条件、ソート、上側タブ状態、ページ状態を `QueryState` として 1 か所へ寄せる。
- `movieData -> MovieRecords[] -> FilteredMovieRecs` の都度組み立てをやめ、read model 更新と view query 適用を分離する。
- `isGetNew=true` の full reload と、検索語変更・ソート変更・watch 差分反映を同じ入口で扱わない。
- まず以下の 3 種に分ける。
  - full snapshot reload
  - query only recompute
  - item diff apply

完了条件:
- watch の少量変更で DB 全件再読込へ戻らない。
- 検索語変更と watch 反映の hot path が別になっている。

## Phase 2: Diff-first 一覧反映

目的:
- 一覧更新時の仕事量を「総件数依存」から「変更件数依存」へ寄せる。

実施内容:
- `MainVM.ReplaceFilteredMovieRecs(...)` を中核にしつつ、一覧反映の前段に `FilteredMovieDiffCoordinator` 相当を置く。
- watch / rescue / manual reload の反映を「追加」「削除」「更新」「順位変更」に分ける。
- `FilterAndSort(..., true)` を watch の既定終端から外し、小規模変更は差分 apply を既定にする。
- 全面再評価が必要な条件だけを明示する。
  - sort key 変更
  - query 条件変更
  - 大量変更しきい値超過
  - DB 切り替え

完了条件:
- watch の 1 件追加や rename で `Refresh()` 全面経路を常に踏まない。
- 「小規模差分」と「全面再評価」の境界がコード上で説明できる。

## Phase 3: Watch 差分パイプライン化

目的:
- watch 完了後の UI 再評価を、全面 reload ではなく差分通知へ変える。

実施内容:
- `WatchScanCoordinator` をさらに前に出し、folder orchestration から UI apply までの中継 DTO を正式化する。
- `CheckFolderAsync(...)` の残タスクから、以下を coordinator / dispatcher へ外出しする。
  - visible-only gate
  - zero-byte 待機
  - first-hit 通知
  - final queue flush
- watch 側の結果を `MovieChangeSet` 相当で返し、UI bridge は change set を受けて小規模 apply だけを行う。
- 大量変更時だけ dirty flag を立て、UI アイドル時に全面再評価へ落とす。

完了条件:
- watch 終端の既定経路が `FilterAndSort(..., true)` ではなくなる。
- `MainWindow.Watcher.cs` は folder scan と queue 調停に集中し、UI apply 詳細を持たない。

## Phase 4: Visible-first 画像供給の徹底

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

## Phase 5: 起動 warm path の再短縮

目的:
- first-page 起動を前提に、再起動直後の「触れるまでの時間」をさらに縮める。

実施内容:
- 起動時 read model を first-page 用と background append 用に明確分離する。
- warm start 用 sidecar catalog を導入する場合は `LocalAppData` 配下に限定し、壊れても DB fallback へ戻す。
- watcher / bookmark / tag index / thumbnail success index の prewarm は、UI 入力可能後に順次開始する。
- `OpenDatafile(...)` 後に必要な同期仕事をさらに削り、「表示」「操作可能」「常駐起動完了」を別イベントに分ける。

完了条件:
- 起動完了を 1 点ではなく、`first-page shown`、`input ready`、`heavy services started` で説明できる。
- 大 DB でも起動直後の操作待ちが減る。

## Phase 6: skin 切り替えの表示・保存完全分離

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

## 6. 優先順位

実装順は次で固定する。

1. Phase 0 計測固定
2. Phase 1 Query state 分離
3. Phase 2 Diff-first 一覧反映
4. Phase 3 Watch 差分パイプライン化
5. Phase 4 Visible-first 画像供給
6. Phase 5 起動 warm path
7. Phase 6 skin 切り替え完全分離

理由:
- 先に watch や skin を個別最適化しても、一覧側が全面再評価中心のままだと効果が頭打ちになるため。

## 7. 直近の着手順

### Step 1

- `FilterAndSortAsync(...)` の呼び出し元を棚卸しし、`full reload`、`query recompute`、`watch reload` の 3 群へ分類する。

### Step 2

- watch 終端の `InvokeFilterAndSortForWatch(...)` を差分 apply 可能な条件でバイパスする設計メモを作る。

### Step 3

- `MovieChangeSet` と `QueryState` の最小 DTO を追加し、`MainWindow` が直接生配列を握る時間を減らす。

### Step 4

- `NoLockImageConverter` の stamp 取得を別 cache に寄せるか、既存 metadata cache を viewport と結ぶかを決める。

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
