# スキン機能進捗メモ (2026-04-07 / 2026-04-10 / 2026-04-11 更新)

## 現在地

- 目標: WhiteBrowser 由来スキン機能を WebView2 で安定表示し、検索・サムネ契約・旧 WB 互換 callback を実運用できる形まで押し上げる。
- 進捗評価: **Phase 1/2 の中核に加え、callback 互換の第一段、操作互換の第1段、host lifecycle の第2段階まで成立**。
- 現在の意味: `SimpleGridWB` を動かすための最小互換から、`TutorialCallbackGrid` や `WhiteBrowserDefault*` fixture を視野に入れた legacy 互換層へ一段進み、選択 / lifecycle / scroll と host refresh の土台も実運用寄りになった。

## 今回の実装反映 (2026-04-10 / 2026-04-11)

### 1. callback 互換を一段厚くした

- `onCreateThum` を起点に、旧 WB スキンが前提にしている callback の呼び口を bridge 側へ寄せた。
- `onSetFocus` / `onSetSelect` は、旧 WB スキン側が扱いやすい引数形へ寄せた。
- `onSkinEnter` とスクロール初期化導線を追加し、skin 起動直後の初期化フローを揃えた。
- `prototype.js` compat に `Insertion.Top` / `Insertion.Bottom` の最小実装を追加し、`TutorialCallbackGrid` など旧 `onCreateThum` 挿入呼びをそのまま受けられるようにした。
- `update/find/sort/addWhere/addOrder/addFilter/removeFilter/clearFilter` の先頭再更新 (`startIndex <= 0`) は、compat runtime 側で `onClearAll` を先に返してから `onUpdate` を流すようにした。
- これにより「`onUpdate` だけ返せる段階」から、「既存 WB skin fixture の描画 callback を順に受け始める段階」へ進んだ。

### 2. 検索以外の操作 API を前進させた

- skin 側からの並び替え要求を、本体の既存検索 / 並び替え導線へ流せる形にした。
- `wb.getProfile` / `wb.writeProfile` / `wb.changeSkin` を MainWindow 側へ接続した。
- `wb.selectThum` は `focusThum` から分離し、WPF 側の現在選択状態と複数選択反映へ接続した。
- `focusedMovieId` を返すようにして、選択解除後に WPF 側で移った実フォーカスへ compat runtime が追従できるようにした。
- これで `find` に続き、一覧 UI が必要とする基本操作を skin 側から段階的に触れるようになった。

### 3. tag 系 API を実装した

- `wb.addTag` / `wb.removeTag` / `wb.flipTag` を bridge 対象へ追加した。
- tag 変更は skin 専用の別実装を増やさず、既存のタグ更新導線と同じ正規化 / 永続化 / UI 再同期を通す形に寄せた。
- DB 更新後は readback で永続化結果を確認し、失敗時はメモリ上の tag を巻き戻して成功扱いにしないようにした。
- compat runtime では tag 変更後に `onModifyTags` を返せるようにし、WB 側スクリプトが更新を受けやすい形にした。

### 4. lifecycle / scroll の第1段階を仕上げた

- `onSkinLeave` / `onClearAll` の意味論と発火順を compat runtime 側で固定した。
- `scroll-id` を読んでスクロール対象を解決し、`wb.scrollTo` を inner pane 前提 skin でも扱いやすい形へ寄せた。
- `multi-select` / `scroll-id` 設定を compat runtime が読めるようにし、旧 WB skin の設定依存挙動を一段吸収した。

### 5. `onUpdateThum` の第1段階を入れた

- 通常サムネ生成成功と rescue 反映成功の両方から、現在表示中の外部 skin へ `onUpdateThum` を dispatch する導線を追加した。
- callback payload は `recordKey / thumbUrl / thumbRevision / thumbSourceKind / sizeInfo` を正本にし、compat runtime では legacy callback が受けやすい位置引数形でも返せるようにした。
- 発火は「外部 skin が有効」「現在表示タブと更新タブが一致」「WPF 側のサムネ path 反映が成功」の条件に絞り、無関係な更新を流し込まない形にした。

### 6. host refresh / lifecycle の第2段階を入れた

- `WhiteBrowserSkinHostControl` が、外部 skin の再 navigate 前と fallback blank 前に `handleSkinLeave` を明示 dispatch するようにした。
- `NavigateToString` と `Clear` は `NavigationCompleted` まで待つようにし、scheduler が document 遷移まで直列化できる形へ寄せた。
- 実 WebView2 統合テストで、host 側の `HandleSkinLeaveAsync` が `focus false -> select false -> clear -> leave` を 1 回だけ返すことを固定した。

### 7. legacy alias を追加した

- DTO / callback payload に、旧 WB スキンがそのまま参照しやすい別名を追加した。
- 代表例:
  - `id`
  - `title`
  - `thum`
  - `exist`
  - `select`
- 既に導入済みの新契約 `recordKey` / `thumbRevision` / `thumbUrl` / 寸法情報は維持し、互換と拡張の両立を優先した。

### 8. 検索条件 API の第1段階を入れた

- `wb.addWhere` を追加し、現在結果へ SQL 風の追加条件を重ねて即時 `onUpdate` へ返す形にした。
- `wb.addOrder` を追加し、WB 互換の `override=0/1` と空文字クリアを受けられるようにした。
- `addOrder("{...}", 0/1)` は SQL 風の `ORDER` 断片を受け、通常の sort 名 / sort ID 指定も扱えるようにした。
- 実装は「本体検索器を増やさず、外部 skin の overlay 条件だけを runtime 側で吸収する」方針に寄せた。

### 9. テストを追加 / 補強した

- callback 互換
- `wb.sort`
- `wb.addWhere` / `wb.addOrder`
- `wb.getProfile` / `wb.writeProfile` / `wb.changeSkin`
- `wb.addTag` / `wb.removeTag` / `wb.flipTag`
- `onUpdateThum`
- legacy alias
- 複数選択反映
- `focusedMovieId`
- lifecycle の発火順
- `scroll-id` 経路
- compat script の callback 回数確認
- `TutorialCallbackGrid` 実 fixture の初回 update / focus / leave clear 統合確認
- `WhiteBrowserDefaultList` 実 fixture の default `onUpdate` / `onCreateThum` / `scroll-id` 統合確認
- `WhiteBrowserDefaultGrid` / `Small` / `Big` 実 fixture の default `onUpdate` fallback / `onCreateThum` / leave clear 統合確認
- `TutorialCallbackGrid -> WhiteBrowserDefaultList` fixture 切替時の旧 DOM 残骸なし確認
- `TutorialCallbackGrid` 同一 fixture 再 navigate 時の `focus false -> select false -> clear -> leave` probe 確認
- MainWindow 経由の `TutorialCallbackGrid` 実 fixture DB切替後再描画確認
- MainWindow 経由の `TutorialCallbackGrid -> WhiteBrowserDefaultList` 実 fixture 切替確認
- MainWindow 経由の `TutorialCallbackGrid` 実 fixture `find / sort / addFilter` 再更新確認
- MainWindow 経由の `TutorialCallbackGrid` 実 fixture minimal reload / `external -> built-in` 確認
- MainDB readback を含む tag 永続化確認

を API service テストと compat script 統合テストで押さえる構成へ進めた。

## 実装済み主要事項

### 基盤

- `skin` 資産とスキン実装ソースを分離済み。
  - 実行資産: `skin\` (`Compat`, `DefaultGridWB`, `SimpleGridWB`)
  - 実装ソース: `WhiteBrowserSkin\`
- 外部 skin の catalog / config 読み込みと、`system.skin` / `profile` を使った skin 永続化を成立済み。

### Host / UI 統合

- `MainWindow` 側の WebView2 外部スキン初期化を安定化。
- host を `Hidden` で仮マウントしてから WebView2 初期化する実機安定化策を導入済み。
- refresh scheduler で skin / DB 切替の揺れを畳む構成を導入済み。
- MainWindow UI 統合テストでは、fixture 用 skin root を差し替えても本番の host prepare / navigate 経路をそのまま通せるようにした。
- `WhiteBrowserSkinRenderCoordinator` で、同一 skin HTML の再表示時に正規化済み document を再利用する第1段キャッシュを導入した。
- runtime 未導入時の fallback 分岐を導入済み。
- runtime 未導入 / skin HTML 欠落 / host 初期化失敗を標準ヘッダー上の診断案内で見分けられる第1段を実装済み。
- fallback 診断通知から、そのまま `再試行` できる導線を追加済み。
- fallback 診断通知から、そのまま `Runtimeを入手` で公式導線を開けるようにした。
- fallback 診断通知から、そのまま `debug-runtime.log` を開ける導線を追加済み。
- 外部スキン表示中の最小ヘッダー (`Host Chrome Minimal`) を導入済み。

### Bridge / API

- `wb.update`
- `wb.find`
- `wb.sort`
- `wb.addWhere`
- `wb.addOrder`
- `wb.addFilter`
- `wb.removeFilter`
- `wb.clearFilter`
- `wb.getInfo`
- `wb.getInfos`
- `wb.getFindInfo`
- `wb.getFocusThum`
- `wb.getSelectThums`
- `wb.getProfile`
- `wb.writeProfile`
- `wb.changeSkin`
- `wb.focusThum`
- `wb.selectThum` (focus から分離、複数選択反映)
- `wb.addTag`
- `wb.removeTag`
- `wb.flipTag`
- `wb.scrollTo`
- `wb.getSkinName`
- `wb.getDBName`
- `wb.getThumDir`
- `wb.trace`

までを bridge 対象に含めた。

### 旧 WB 互換

- `onUpdate` だけでなく、`onCreateThum` を軸に callback 互換を拡張した。
- `onSetFocus` / `onSetSelect` / `onSkinEnter` / `onSkinLeave` / `onClearAll` 側の接続を進めた。
- `onUpdateThum` は通常生成 / rescue 反映から現在表示タブだけへ返す第1段階を実装した。
- tag 変更時の `onModifyTags` 呼び口を compat runtime 側へ追加した。
- host 側の明示 lifecycle 契約と、実 WebView2 での leave / clear 順序確認を追加した。
- 旧 WB スキンが参照する alias を追加し、既存 fixture を無修正に近い形で通しやすくした。

### サムネ契約

- `dbIdentity`, `recordKey`, `thumbRevision`, `?rev=` を使う契約を固定済み。
- `thumbUrl`, `thumbSourceKind`, 寸法 DTO を含む正本 service を導入済み。
- `thum.local` 経由の managed / external / placeholder サムネ配信を実機で確認済み。
- GDI 枯渇を避けるためのサイズ情報キャッシュまで反映済み。

### 検索 / 並び替え

- 検索 bridge を最小実装し、完了待ちを確実化済み。
- skin 側からの `wb.sort` を本体の並び替え導線へ接続済み。
- `wb.addFilter` / `wb.removeFilter` / `wb.clearFilter` は、exact tag 構文で `SearchKeyword` と filter 一覧を同期する第1段を実装した。
- 本メモでは mixed-query を「自由入力検索と exact tag filter が同時に入った検索状態」として扱う。
- quoted phrase や否定 quoted phrase を含む mixed-query でも、exact tag filter の追加 / 除去で自由入力側を壊さないようにした。
- `wb.getFindInfo` で検索語 / sort / `addWhere` / `addOrder` / 件数を取得できるようにした。
- `wb.getInfos` は `movieIds` / `recordKeys` に加え、`startIndex + count` で必要範囲だけ返せるようにした。
- compat runtime の range 系 API は、`count` 省略時に `g_thumbs_limit` / `defaultThumbLimit` を既定値として送るようにした。
- `SimpleGridWB` は `wb.update(0)` / `wb.find(keyword, 0)` を既定件数ベースへ寄せ、追加ページは `wb.getInfos(startIndex)` で追記する第1段を反映した。
- compat runtime の既定 `onUpdate` fallback は、`onCreateThum` だけを持つ skin でも `startIndex > 0` の `update` を append として扱えるようにした。
- `wb.getFocusThum` / `wb.getSelectThums` で現在 focus と複数選択 ID の取得を可能にした。
- `getFindInfo.filter` は現時点の filter 一覧を返す。
- native タグバー checked 状態は、exact tag 構文を正本にする第1段まで寄せた。
- skin 切替では native search 状態として維持し、DB 未選択時だけ overlay fallback へ落ちる第1段階実装である。

### テスト

- UI 統合テスト
- runtime bridge 統合テスト
- compat script 統合テスト
- API service テスト
- サムネ契約テスト
- callback 互換 / legacy alias / sort / profile / skin 切替 / 複数選択 / lifecycle / scroll / search info / focus getter / selected ids / filter overlay / DB切替 / external->external / external->built-in / minimal reload 回帰テスト
- runtime 未導入 / html missing の診断案内回帰テスト

まで含めて検証面を強化した。

## 到達点の見立て

- 4/7 時点では「次の山」だった callback 互換強化 / `wb.sort` / tag 系 API が、今回の作業系列で実装ラインへ上がった。
- これで外部 skin は「表示できる」だけではなく、「既存 WB skin の JavaScript がどこまで無修正で通るか」を現実的に押し上げる段階へ入った。
- 一方で、まだ **旧 WB 完全互換が終わったわけではない**。残件は明確に残っている。

## 未完 (次に着手する候補)

1. 実ホスト受け入れ確認
   - DB切替 / external->external / external->built-in / minimal reload は自動回帰へ載せた
   - 残りは WebView2 実ランタイム再初期化を含む手動確認
2. filter 互換の厚み出し
   - native タグバー以外の自由入力検索と checked 状態の境界整理
3. 大量件数対策
   - `SimpleGridWB` では初回ページ + `getInfos(startIndex)` の追加ページ読込を実 host で確認済み
   - `WhiteBrowserDefaultList` では `onCreateThum` だけの既定 fallback でも `update(2, 1)` を append として描画できることを実 fixture で確認済み
   - `TutorialCallbackGrid` では actual scroll 後だけ `seamless-scroll` 追記し、先頭 focus を保てることを MainWindow 実 host で確認済み
   - `WhiteBrowserDefaultList` では config の `seamless-scroll : 2` だけでも scroll 起点の追記が動くことを実 fixture で確認済み
   - MainWindow 実 host でも `200件初回 + update(200, 1)` の追記を確認済み
   - 差分更新
   - 仮想スクロール
   - 可視範囲優先ロード
   - DOM 膨張抑制
4. runtime 未導入時の案内と診断導線の強化
   - 実ランタイム再初期化を含む手動受け入れ確認

## 更新メモ

- 2026-04-07: Phase 1/2 の中核成立を確認。
- 2026-04-09: サムネサイズ情報キャッシュで GDI 枯渇抑制を反映。
- 2026-04-10: callback 互換強化、`wb.sort` 追加、legacy alias 対応、テスト追加に追随。
- 2026-04-11: `selectThum` 分離、複数選択反映、`onSkinLeave` / `onClearAll` 固定、`scroll-id` / `wb.scrollTo` 強化、tag 系 API 実装に追随。
- 2026-04-11: `onUpdateThum` 第1段階、compat payload 契約、通常生成 / rescue 反映からの callback dispatch、関連テスト 128 件通過を反映。
- 2026-04-11: host 側の明示 `handleSkinLeave`、Navigate/blank の `NavigationCompleted` 待ち、実 WebView2 lifecycle 統合テスト、関連テスト 129 件通過を反映。
- 2026-04-11: `wb.addWhere` / `wb.addOrder` 第1段階、SQL 風 overlay 条件、`override` / 空文字クリア、関連テスト 131 件通過を反映。
- 2026-04-11: `wb.getFindInfo` / `wb.getFocusThum` / `wb.getSelectThums`、overlay reset、文字列 movie id の tag API 互換、関連テスト 136 件通過を反映。
- 2026-04-11: `wb.addFilter` / `wb.removeFilter` / `wb.clearFilter` 第1段階、mixed-query の `SearchKeyword` 同期、native タグバー checked 第1段、DB切替 / external->external / external->built-in / minimal reload 統合テスト追加、関連テスト 164 件通過を反映。
- 2026-04-11: runtime 未導入 / html missing / host 初期化失敗の診断案内導線、標準ヘッダー通知、fallback からの `再試行` / `Runtimeを入手` / `ログを開く` 導線、関連テスト 168 件通過を反映。
- 2026-04-11: quoted phrase / 否定 quoted phrase を含む mixed-query と exact tag filter の共存を補強し、関連テスト 173 件通過を反映。
- 2026-04-11: `TutorialCallbackGrid` 実 fixture の `onSkinEnter -> update -> onCreateThum -> focusThum -> leave clear` 統合テストを追加した。
- 2026-04-11: `TutorialCallbackGrid` の再 `update(0, ...)` で DOM を積み増さず、compat runtime 側の先頭再更新 clear を通して再描画できることを実 fixture 統合テストで固定した。
- 2026-04-11: compat script 統合テストで、先頭再更新 (`startIndex <= 0`) だけ `clear -> onUpdate`、追記更新 (`startIndex > 0`) は append のまま、を固定した。
- 2026-04-11: `WhiteBrowserDefaultList` 実 fixture の default `onUpdate` / `onCreateThum` / `scroll-id : scroll` / leave clear 統合テストを追加した。
- 2026-04-11: `WhiteBrowserDefaultGrid` / `Small` / `Big` 実 fixture の default `onUpdate` fallback / `onCreateThum` / leave clear 統合テストを追加した。
- 2026-04-11: `TutorialCallbackGrid -> WhiteBrowserDefaultList` の fixture 切替で、旧 DOM 残骸を残さず次の描画へ移れることを host 統合テストで追加確認した。
- 2026-04-11: `TutorialCallbackGrid` の同一 fixture 再 navigate で、`focus false -> select false -> clear -> leave` を probe で捕捉しつつ、再描画後に旧 DOM が残らないことを host 統合テストで確認した。
- 2026-04-11: MainWindow UI 統合テストへ fixture 用 skin root 差し替え導線を追加し、`TutorialCallbackGrid` の DB切替後再描画と `TutorialCallbackGrid -> WhiteBrowserDefaultList` 切替を、実 fixture の DOM まで確認した。関連 targeted 36 件通過。
- 2026-04-12: `TutorialCallbackGrid` を MainWindow 経由の実 fixture として、`find / sort / addFilter` 再更新でも旧 DOM 残骸を残さず描画更新できることを確認した。`find` と `addFilter` は実 MainDB を使って `SearchExecutionController` の検索経路まで通し、関連 broad 回帰 39 件通過、追加 targeted 3 件通過を反映。
- 2026-04-12: `TutorialCallbackGrid` を MainWindow 経由の実 fixture として、minimal reload と `external -> built-in` まで DOM / host 表示で確認した。あわせて `MainWindow.WebViewSkin.Chrome` の reload / retry 導線を `ClearAsync` 完了待ちへ寄せ、blank 遷移と再 navigate の race を抑止した。関連 broad 回帰 41 件通過、reload 系 targeted 3 件通過を反映。
- 2026-04-12: `WhiteBrowserSkinRenderCoordinator` に、同一 skin HTML の `ReadAllBytes + Normalize` を再利用する第1段キャッシュを追加した。`WhiteBrowserSkinRenderCoordinatorTests` へ再利用と更新差し替え回帰を追加し、関連 targeted 6 件通過を反映。
- 2026-04-12: `wb.getInfos` に `startIndex + count` の範囲取得を追加し、`movieIds` / `recordKeys` と併存できる形へ拡張した。compat script も `wb.getInfos([ids])` / `wb.getInfos(startIndex, count)` / `wb.getInfos({ recordKeys })` の投げ分けに対応し、関連 targeted 3 件通過を反映。
- 2026-04-12: compat runtime の range 系 API が、`count` 省略時に `g_thumbs_limit` / `defaultThumbLimit` を既定値として送るようにした。`wb.find("idol")` と `wb.getInfos(120)` が既定 200 件で送られることを compat 統合テストで固定した。
- 2026-04-12: `SimpleGridWB` を、初回 `wb.update(0)` / `wb.find(keyword, 0)` と追加 `wb.getInfos(startIndex)` で段階読込する baseline skin へ更新した。MainWindow 実 host 統合テストで、`200 / 260 items -> 260 items` の追記描画と、追加ページ後の `find` で旧 card 残骸を残さず先頭結果へ戻せることを確認した。関連 targeted 5 件通過。
- 2026-04-12: compat runtime の既定 `onUpdate` fallback を、`onCreateThum` だけを持つ skin でも `startIndex > 0` の `update` を append として扱えるようにした。compat script 統合テストと `WhiteBrowserDefaultList` 実 fixture 統合テストで、`update(2, 1)` の追記描画を確認した。関連 targeted 4 件通過。
- 2026-04-12: MainWindow 実 host 統合テストでも `WhiteBrowserDefaultList` の `200件初回 + update(200, 1)` を確認し、既存 200 行を残したまま `Movie201.mp4` を末尾へ追記できることを固定した。関連 targeted 3 件通過。
- 2026-04-12: MainWindow 実 host 統合テストでも `TutorialCallbackGrid` の `200件初回 + update(200, 1)` 追記と、その直後の `wb.find("Movie201", 0)` 先頭復帰を確認し、旧 thum 残骸なしで `Movie201.mp4` へ戻せることを固定した。関連 targeted 5 件通過。
- 2026-04-12: compat runtime の `seamless-scroll` は、初回 `onUpdate` 後に勝手に追記せず、実際の scroll 発火時だけ次ページ要求する形へ整理した。`TutorialCallbackGrid` の MainWindow 実 host seamless scroll と、`WhiteBrowserDefaultList` の config 駆動 seamless scroll を targeted 4 件で固定し、その後の広め回帰 79 件通過を確認した。

## 参考ドキュメント

- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `WhiteBrowserSkin/Docs/ExternalSkinApiUsageSummary_2026-04-07.md`
- `WhiteBrowserSkin/Docs/調査結果_WebView2_WBskin再調査_実装進捗反映_2026-04-02.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
- `WhiteBrowserSkin/Docs/障害対応_WebView2サムネ契約_GDI枯渇抑制_2026-04-09.md`
