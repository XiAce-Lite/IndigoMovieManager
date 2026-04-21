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
   - `SimpleGridWB` では「続きを読み込む」だけでなく、scroll でも `getInfos(startIndex)` を取りに行ける baseline へ更新済み
   - `SimpleGridWB` では追加要求が空振りした時、以後の scroll / button 追加要求を自律停止できるようにした
   - `SimpleGridWB` では追記時に残り件数だけ要求し、append だけで済む時は既存 DOM を残すようにした
   - `SimpleGridWB` では offscreen thumb を `data-thumb-url` へ退避し、可視範囲へ入った時だけ `src` を付ける第1段を反映した
   - `SimpleGridWB` では `onUpdateThum(recordKey, thumbUrl)` を受けた時、offscreen thumb は `data-thumb-url` と state だけ差し替え、可視範囲へ入った後に新しい `src` を昇格できるようにした
- `SimpleGridWB` では、可視範囲から十分離れた thumb は `src` を外して `data-thumb-url` へ戻し、見えている範囲だけ画像を保持する第2段も反映した
- `SimpleGridWB` では、可視範囲外の card に `is-distant` を付けて本文詳細を休ませ、見えた時だけ `.card__sub` と `.card__tags` を復帰させる第1段も反映した
 - compat runtime では、`onCreateThum` 未実装の外部 skin に最小サムネ生成 fallback を持たせ、`onUpdateThum` 未実装でも `img{id}` / `#thum{id} img` / `data-record-key` へ既定 `src` 差し替えを行えるようにした
   - `WhiteBrowserDefaultList` では `onCreateThum` だけの既定 fallback でも `update(2, 1)` を append として描画できることを実 fixture で確認済み
   - `TutorialCallbackGrid` では actual scroll 後だけ `seamless-scroll` 追記し、先頭 focus を保てることを MainWindow 実 host で確認済み
   - `WhiteBrowserDefaultList` では config の `seamless-scroll : 2` だけでも scroll 起点の追記が動くことを実 fixture で確認済み
   - `WhiteBrowserDefaultGrid` / `Small` / `Big` でも config の `seamless-scroll : 2` だけで scroll 起点の追記が動くことを実 fixture で確認済み
   - MainWindow 実 host でも `WhiteBrowserDefaultList` の config 駆動 `seamless-scroll` 追記を確認済み
   - MainWindow 実 host でも `WhiteBrowserDefaultList` の `seamless-scroll` 追記後 `find(..., 0)` 先頭復帰を確認済み
   - MainWindow 実 host では `WhiteBrowserDefaultList` の config 駆動 `seamless-scroll` 追記を代表ケースとして確認済み
   - `WhiteBrowserDefaultGrid` / `Small` / `Big` は runtime bridge 側で config 駆動 `seamless-scroll` と検索後 `find(..., 0)` 先頭復帰を固定し、MainWindow 実 host は `List` / `TutorialCallbackGrid` / `SimpleGridWB` を代表ケースとして持つ
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
- 2026-04-17: runtime bridge 実 host でも `TagInputRelation` の `Get` 後 dirty state から `wb.changeSkin("#umlFindTreeEve")` して input / 候補を tree / footer 側へ持ち越さないこと、`umiFindTreeEve` の `onRegistedFile -> Refresh()` 後 dirty state から `wb.changeSkin("#TagInputRelation")` して tree / footer を input / candidate 側へ持ち越さないことを focused 4 件通過で確認した。
- 2026-04-17: runtime bridge 実 host でも `TagInputRelation` は `onSkinLeave -> 再 navigate -> onExtensionUpdated` 後に、入力欄を空のまま初期候補 3 件を重複なく再生成できることを確認した。`umiFindTreeEve` は `onModifyTags -> Refresh()` 後の dirty state で `onSkinLeave -> 再 navigate` しても、`fresh-tag` を含む tree / footer を 1 回だけ再生成し、重複表示しないことを focused 4 件通過で確認した。
- 2026-04-19: runtime bridge 実 host でも `umiFindTreeEve` は `onModifyTags -> Refresh()` 後に bare な `onClearAll -> Refresh()` を流しても、`fresh-tag` を含む更新済み tag tree と `ClearCache` footer を 1 回だけ再生成し、重複表示しないことを focused 1 件通過で確認した。tag 更新後 clear 再描画の非 terminal 境界も正本化できた。
- 2026-04-19: runtime bridge 実 host でも `TagInputRelation` は `ButtonGet()` で候補 4 件へ拡張した後に `onClearAll` / `onSkinLeave` して再入すると、入力欄を空のまま初期候補 3 件を重複なく再生成できることを focused で確認した。Get 後の terminal reset も Save 後と同じ粒度で揃えた。
- 2026-04-17: runtime bridge 実 host でも `TagInputRelation` は `wb.changeSkin("MissingSkin")` 失敗時に入力欄と候補表示を現在 skin のまま維持し、`umiFindTreeEve` は `wb.changeSkin("MissingSkin")` 失敗時に `fresh-tag` を含む更新済み tree / footer を現在 skin のまま維持することを focused 4 件通過で確認した。
- 2026-04-17: runtime bridge 実 host でも `umiFindTreeEve` は `onRegistedFile -> Refresh()` 後の dirty state で `onSkinLeave -> 再 navigate` しても、`fresh-series` の tag tree と footer を 1 回だけ再生成し、重複表示しないことを focused 3 件通過で確認した。
- 2026-04-19: runtime bridge 実 host でも `umiFindTreeEve` は `onRegistedFile -> Refresh()` 後に `onClearAll` または `onSkinLeave` を挟んで `wb.changeSkin("#TagInputRelation")` しても、更新済み tree / footer を input / candidate 側へ持ち越さないことを focused 2 件通過で確認した。register 後の terminal success も `tag / path / remove` と同じ粒度へそろった。
- 2026-04-19: runtime bridge 実 host でも `umiFindTreeEve` は `onRegistedFile -> Refresh()` 後に `onClearAll` または `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、`fresh-series` を含む更新済み tree / footer を現在 skin のまま維持することを focused 2 件通過で確認した。register 後の terminal failure も実測どおりで固定できた。
- 2026-04-19: runtime bridge 実 host でも `umiFindTreeEve` は `onRegistedFile -> Refresh()` 後に bare な `onClearAll -> Refresh()` を流しても、`fresh-series` の新規 tag tree と `ClearCache` footer を 1 回だけ再生成し、重複表示しないことを focused 1 件通過で確認した。register 後 clear 再描画の非 terminal 境界も正本化できた。
- 2026-04-19: runtime bridge 実 host でも `umiFindTreeEve` は `onRemoveFile -> onClearAll -> Refresh()` 後も削除済み tree を復活させず、`ClearCache` footer を維持することを focused 2 件通過で確認した。
- 2026-04-19: runtime bridge 実 host でも `umiFindTreeEve` は `onModifyPath -> onClearAll -> Refresh()` 後も更新済み folder tree と footer を重複表示しないことを focused 2 件通過で確認した。
- 2026-04-17: runtime bridge 実 host でも `umiFindTreeEve` は `onModifyPath -> Refresh()` 後の dirty state で `onSkinLeave -> 再 navigate` しても、更新後の folder tree を 1 回だけ再生成し、`fresh` を重複表示しないことを focused 3 件通過で確認した。
- 2026-04-17: runtime bridge 実 host でも `umiFindTreeEve` は `onRemoveFile -> Refresh()` 後の dirty state で `onSkinLeave -> 再 navigate` しても、削除済み `series-a` tree を復活させず、footer も 1 回だけ再生成することを focused 3 件通過で確認した。
- 2026-04-12: `WhiteBrowserSkinRenderCoordinator` に、同一 skin HTML の `ReadAllBytes + Normalize` を再利用する第1段キャッシュを追加した。`WhiteBrowserSkinRenderCoordinatorTests` へ再利用と更新差し替え回帰を追加し、関連 targeted 6 件通過を反映。
- 2026-04-12: `wb.getInfos` に `startIndex + count` の範囲取得を追加し、`movieIds` / `recordKeys` と併存できる形へ拡張した。compat script も `wb.getInfos([ids])` / `wb.getInfos(startIndex, count)` / `wb.getInfos({ recordKeys })` の投げ分けに対応し、関連 targeted 3 件通過を反映。
- 2026-04-12: compat runtime の range 系 API が、`count` 省略時に `g_thumbs_limit` / `defaultThumbLimit` を既定値として送るようにした。`wb.find("idol")` と `wb.getInfos(120)` が既定 200 件で送られることを compat 統合テストで固定した。
- 2026-04-12: `SimpleGridWB` を、初回 `wb.update(0)` / `wb.find(keyword, 0)` と追加 `wb.getInfos(startIndex)` で段階読込する baseline skin へ更新した。MainWindow 実 host 統合テストで、`200 / 260 items -> 260 items` の追記描画と、追加ページ後の `find` で旧 card 残骸を残さず先頭結果へ戻せることを確認した。関連 targeted 5 件通過。
- 2026-04-12: compat runtime の既定 `onUpdate` fallback を、`onCreateThum` だけを持つ skin でも `startIndex > 0` の `update` を append として扱えるようにした。compat script 統合テストと `WhiteBrowserDefaultList` 実 fixture 統合テストで、`update(2, 1)` の追記描画を確認した。関連 targeted 4 件通過。
- 2026-04-12: MainWindow 実 host 統合テストでも `WhiteBrowserDefaultList` の `200件初回 + update(200, 1)` を確認し、既存 200 行を残したまま `Movie201.mp4` を末尾へ追記できることを固定した。関連 targeted 3 件通過。
- 2026-04-12: MainWindow 実 host 統合テストでも `TutorialCallbackGrid` の `200件初回 + update(200, 1)` 追記と、その直後の `wb.find("Movie201", 0)` 先頭復帰を確認し、旧 thum 残骸なしで `Movie201.mp4` へ戻せることを固定した。関連 targeted 5 件通過。
- 2026-04-12: compat runtime の `seamless-scroll` は、初回 `onUpdate` 後に勝手に追記せず、実際の scroll 発火時だけ次ページ要求する形へ整理した。`TutorialCallbackGrid` の MainWindow 実 host seamless scroll と、`WhiteBrowserDefaultList` の config 駆動 seamless scroll を targeted 4 件で固定し、その後の広め回帰 79 件通過を確認した。
- 2026-04-12: `WhiteBrowserDefaultGrid` / `Small` / `Big` でも config 駆動 `seamless-scroll` の追記を実 fixture 統合テストで固定した。score 表示あり fixture でも追記後 DOM を確認し、targeted 6 件通過を確認した。
- 2026-04-12: MainWindow 実 host 統合テストでも `WhiteBrowserDefaultGrid` / `Small` / `Big` の config 駆動 `seamless-scroll` 追記を固定し、`Movie201.mp4` / `No.201 : Movie201.mp4` と score 表示の追記後 DOM を確認した。targeted 4 件通過を確認した。
- 2026-04-12: MainWindow 実 host 統合テストでも `WhiteBrowserDefaultList` の config 駆動 `seamless-scroll` 追記を固定し、`scroll-id : scroll` を使う list skin でも `Movie201.mp4` を末尾へ追記できることを確認した。targeted 4 件通過を確認した。
- 2026-04-12: MainWindow 実 host 統合テストでも `WhiteBrowserDefaultList` の `seamless-scroll` 追記後 `wb.find("Movie201", 0)` を確認し、`onCreateThum` だけの既定 fallback skin でも旧 row 残骸を残さず単一結果へ戻せることを固定した。targeted 2 件通過を確認した。
- 2026-04-12: `SimpleGridWB` は scroll でも追加ページを読めるようにし、MainWindow 実 host 統合テストで `200 / 260 items -> 260 items` の自動追記を確認した。既存のボタン導線と追加ページ後 `find` reset も維持し、targeted 3 件通過を確認した。
- 2026-04-12: MainWindow 実 host 統合テストで `WhiteBrowserDefaultList` の `wb.find("Movie", 0)` 後でも `seamless-scroll` で `Movie260.mp4` まで継続追記できることを確認した。検索状態のまま `scroll-id : scroll` 経路で追加ページを積める。
- 2026-04-12: MainWindow 実 host 統合テストで `SimpleGridWB` は検索後でも scroll で `260 items` まで継続追記できることを確認した。`検索: "Movie"` 状態と追加ページ完了表示が両立する。
- 2026-04-12: `WhiteBrowserDefaultGrid` / `Small` / `Big` は実 WebView2 runtime bridge 統合テストで `wb.find("Movie", 0) -> seamless-scroll -> update(startIndex=2)` を固定し、既定 fallback skin 群でも検索後 `find("Movie201", 0)` 先頭復帰まで確認した。`Grid / Small / Big` の深い scroll 意味論は runtime bridge 側を正本とする。
- 2026-04-12: `WhiteBrowserSkinHostControl` に WebView2 実体の明示 dispose を追加し、MainWindow close 時は host control ごと破棄するよう整理した。あわせて `MainWindowWebViewSkinIntegrationTests` の config 単独 `seamless-scroll` は `WhiteBrowserDefaultList` を代表ケースへ整理し、`WhiteBrowserDefaultGrid` / `Small` / `Big` は runtime bridge で config 駆動を保持しつつ、MainWindow では検索後 `seamless-scroll` と `find` reset を維持する構成へ寄せた。
- 2026-04-12: 上記整理後、MainWindow は `List` / `TutorialCallbackGrid` / `SimpleGridWB` を代表ケースとして持ち、`Grid / Small / Big` は runtime bridge 側で scroll / reset 意味論を固定する構成へ整理した。`MainWindowWebViewSkinIntegrationTests` は 42/42、`WhiteBrowserSkinRuntimeBridgeIntegrationTests | WhiteBrowserSkinCompatScriptIntegrationTests | WhiteBrowserSkinEncodingNormalizerTests | WhiteBrowserSkinRenderCoordinatorTests` は 29/29、combined broad も 71/71 通過を確認した。
- 2026-04-12: compat runtime の `seamless-scroll` と `SimpleGridWB` に、空振り追記後は次回要求を止めるガードを追加した。compat script 統合テストで `update(2, 2)` 空振り後に再要求が残らないことを固定し、MainWindow 実 host の `SimpleGridWB` でも空振り後の再要求停止を確認した。
- 2026-04-12: compat runtime の `seamless-scroll` は、残り件数が分かる時は `update(startIndex, remainingCount)` で不足分だけ要求するようにした。`SimpleGridWB` も `wb.getInfos(startIndex, remainingCount)` を使い、MainWindow 実 host で `200 -> 260` 追記時に `200:60` だけ要求しつつ既存 card DOM を保持できることを確認した。
- 2026-04-12: `SimpleGridWB` に可視範囲優先 thumb 読込の第1段を追加した。初回描画では offscreen thumb を `data-thumb-url` のまま保留し、scroll 後に末尾 thumb が `src` へ昇格することを MainWindow 実 host で確認した。
- 2026-04-12: `SimpleGridWB` に `onUpdateThum(recordKey, thumbUrl)` の差分更新導線を追加した。MainWindow 実 host で、offscreen の末尾 thumb は DOM 全体を再描画せず `data-thumb-url` と state だけ先に更新し、その後 scroll で可視範囲へ入った時に新しい `src` へ昇格できることを確認した。
- 2026-04-16: `DebugRuntimeLog` に async flow scope を追加し、`RefreshExternalSkinHostPresentationAsync(...)` の `requestTraceId` を `skin-catalog` / `skin-db` のログ行へ自動伝播できるようにした。`debug-runtime.log` は連番に加え、同一 refresh 中の `trace=rqXXXX` をカテゴリ横断で読み返せる。関連 focused は 27/27 通過。
- 2026-04-16: `DebugRuntimeLog` の async flow scope に対して、並列タスク間で trace が混線しない focused test を追加した。あわせて `skin-webview` に `refresh begin / refresh end` 要約ログを足し、1 回の外部 skin refresh を `trace=rqXXXX` 単位で前から追いやすくした。関連 focused は 28/28 通過。
- 2026-04-16: `skin-webview` の `refresh end` に `elapsed_ms` を追加し、`trace=rqXXXX` ごとに切り替え 1 回の所要時間を読めるようにした。関連 focused は 28/28 維持。
- 2026-04-16: `WhiteBrowserSkinCatalogService` は nonstandard な html 名でも前回 `HtmlPath` を優先再利用するようにし、snapshot 作成時の fallback `EnumerateFiles` を減らした。custom html 名の focused test を追加し、catalog focused は 11/11 通過した。
- 2026-04-16: `WhiteBrowserSkinStatePersistRequest` に trace を追加し、queue をまたぐ persister 側の `skin-db` ログでも `trace=rqXXXX` を維持できるようにした。`WhiteBrowserSkinStatePersisterTests` を含む focused は 39/39 通過した。
- 2026-04-16: `DebugRuntimeLog` の ambient counter を追加し、`refresh end` で `catalog_hit / catalog_miss / persist_enqueued / persist_fallback_applied` を要約できるようにした。shared UI 初期化競合を避けた focused は 40/40 通過した。
- 2026-04-16: `refresh end` の ambient 要約は `catalog_reused / catalog_skipped / catalog_signature_ms / catalog_load_ms` まで読めるようにし、`catalog_*_ms` は trace 内の合計値として扱うようにした。`trace=rqXXXX` の end 1 行だけで、cache miss 時の再利用量と catalog 側の処理コストも見返せる。
- 2026-04-16: `refresh end` には `skinResolved` と短い `dbKey` も載せるようにした。フルパスを追わなくても、どの skin / DB の切り替えだったかを end 1 行で読み返せる。
- 2026-04-16: `refresh end` に `outcome=applied / fallback / standard` を追加した。件数や時間だけでなく、外部 skin refresh が最終的に host 表示 / fallback / 標準表示のどれで終わったかを end 1 行で判別できる。
- 2026-04-12: `SimpleGridWB` の可視範囲優先 thumb 読込を第2段へ進め、可視範囲から外れた先頭 thumb は `src` を外して `data-thumb-url` へ戻し、末尾 thumb は scroll 後に昇格する形へ整理した。MainWindow 実 host で、先頭 thumb の降格と末尾 thumb の昇格が両立することを確認した。
- 2026-04-12: `SimpleGridWB` の card 軽量化の第1段として、可視範囲外 card に `is-distant` を付けて本文詳細を休ませるようにした。MainWindow 実 host で、先頭 card は scroll 後に `is-distant` 化し、末尾 card は逆に復帰することを確認した。
- 2026-04-12: compat runtime に、`onCreateThum` 未実装の外部 skin 向け最小サムネ生成 fallback と、`onUpdateThum` 未実装でも慣例 DOM へ `src` を差し替える fallback を追加した。compat 統合テストと `WhiteBrowserDefaultList` 実 fixture で、callback 未実装でも `img77` のサムネ表示と後追い差し替えが通ることを確認した。
- 2026-04-12: compat runtime の `onUpdateThum` は、custom callback が空振りした時だけ既定 DOM 更新へ後段 fallback するようにした。`Search_table` / `Alpha2` 系の旧 `onUpdateThum(id, src)` 前提を壊さず、legacy callback が `db-main:77` を受けて空振りしても `img77` の `src` を差し替えられることを compat 統合テストで確認した。
- 2026-04-12: compat runtime は `#view` を持たない skin でも最小一覧コンテナを自動生成できるようにした。`TagInputRelation` / `umiFindTreeEve` のような拡張寄り HTML でも、最低限の動画サムネ表示面を差し込める入口を確保し、compat 統合テストで `data-imm-generated-view="true"` と `img77` 生成を確認した。
- 2026-04-12: compat runtime に旧 sync 前提向け shim を追加した。`getProfile("key", fallback)` は fallback を同期値として返しつつ非同期取得も継続し、`getFindInfo` / `getFocusThum` / `getSelectThums` は cached 値を即時参照できるようにした。`Chappy` / `Alpha2` の旧 WB 前提を compat 側で吸収する形である。
- 2026-04-12: compat `prototype.js` に `Element.update`、`String.escapeHTML`、`Array.clone`、`Array.inspect`、`Insertion.Before/After`、`$` の `name` fallback を追加した。`Alpha2` の form/name 参照と prototype 依存を最小面で救い、実 build 出力 skin の初回サムネ表示へ繋げた。
- 2026-04-12: build 出力 skin を使う runtime bridge 統合テストで、`DefaultSmallWB` / `Search_table` / `TagInputRelation` / `umiFindTreeEve` / `Alpha2` / `Chappy` の初回サムネ表示を確認した。`Search_table` と `Alpha2` では legacy `onUpdateThum` の差分サムネ更新も確認し、「当初未表示だった 6 本は初回サムネ表示を通した」段階へ到達した。
- 2026-04-12: build 出力 skin を使う runtime bridge 統合テストで、`DefaultSmallWB` / `Chappy` / `Search_table` / `Alpha2` の差分サムネ更新を確認した。custom `onUpdateThum` を持つ skin と、既定 fallback に頼る skin の両方で `img77` の `src` 差し替えが通ることを固定した。
- 2026-04-12: `wb.getRelation` の最小実装を追加し、visible movies からタイトル近傍とタグを返せるようにした。`TagInputRelation` 向けに compat 側へ `movieInfoCache` / `relationCache` と `onExtensionUpdated` 前 prefetch を入れ、実 build 出力 skin で旧 WB 互換の候補タグ生成まで確認した。
- 2026-04-12: `TagInputRelation` は実 build 出力 skin の runtime bridge 統合テストで、`Include` が選択中動画の tags を input へ反映し、`Save` が `wb.addTag(0, tag)` を選択動画へ流して入力欄をクリアできることまで確認した。候補表示だけでなく操作系も旧同期前提のまま通る段へ進んだ。
- 2026-04-12: `TagInputRelation` は `Get` で relation 候補を 20 -> 30 件相当へ拡張し、候補クリックで input 反映と候補 DOM 除去まで確認した。compat 側では extension 向け relation cache を 20 / 30 の両方温め、`prototype.js` へ `Element.remove` 最小互換を追加して、旧 Prototype 前提の候補クリックを崩さず通した。
- 2026-04-12: compat runtime に `Array.flatten` / `uniq` / `each`、`$F`、`Form.Element.setValue` / `clear` を最小追加し、古い Prototype 前提の extension skin を JS 側 shim で吸収する形へ寄せた。
- 2026-04-12: compat runtime に `getDBName` / `getSkinName` の同期風 thenable、`getInfos(0,-1,...)` 旧署名吸収、`readFile` / `writeFile` の仮想ファイル、`getWatchList` の最小 shim を追加した。`umiFindTreeEve` が求める旧 WB extension API を JS 側で吸収し、cache / tree 生成に必要な同期前提を崩さず通せる形へ寄せた。
- 2026-04-12: `#TagInputRelation` / `#umlFindTreeEve` のようにフォルダ名へ `#` を含む skin は、`skin.local` の `<base href>` が fragment 扱いされて相対 JS/CSS 読込が崩れる問題があった。`WhiteBrowserSkinHostPaths.BuildSkinBaseUri(...)` で path segment を URL encode する fix を入れ、`umlFindTree.js` / `libs/*.js` が正しく読めるようにした。
- 2026-04-12: `umiFindTreeEve` は build 出力 skin の runtime bridge 統合テストで、`onSkinEnter` から `Folders` / `Tags` tree と footer (`ClearCache` など) を生成できるところまで確認した。これにより、当初未表示だった 6 本のうち extension 系は `TagInputRelation` が旧 WB 互換候補タグ生成、`umiFindTreeEve` が旧 WB 互換 tree / footer 生成まで前進した。
- 2026-04-12: `umiFindTreeEve` は `onModifyTags` callback で仮想 cache を更新した後、`Refresh()` から tag tree へ反映できるところまで runtime bridge 統合テストで確認した。extension 系 skin が「起動できる」だけでなく、更新 callback に追従できる段まで進んだ。
- 2026-04-12: compat runtime は `onRegistedFile` callback だけ、事前に `wb.getInfo(id)` を prefetch してから呼ぶようにした。`umiFindTreeEve` が期待する旧同期前提の `wb.getInfo(id)` を崩さず、新規登録後 `Refresh()` で `fresh-series` のような新規 tag tree を追加できることを runtime bridge 統合テストで確認した。
- 2026-04-12: `umiFindTreeEve` は `onRemoveFile` / `onModifyPath` callback 後も `Refresh()` で tree を更新できることを runtime bridge 統合テストで確認した。`series-a` の tag tree 削除と `fresh` folder 反映まで見えており、tree 系 callback は一通り追従できる段まで進んだ。
- 2026-04-12: `WhiteBrowserSkinMovieDto` に legacy lowercase alias `artist` / `drive` / `dir` / `kana` / `tags` を追加し、MainWindow 実 host 経由でも build 出力 extension skin が旧 WB 前提のフィールド名で動けるようにした。`umiFindTreeEve` の `onSkinEnter` が `o.tags.join(...)` で落ちていた問題は、この DTO 契約補強で解消した。
- 2026-04-12: MainWindow 実 host 統合テストに `#TagInputRelation` と `#umlFindTreeEve` を追加した。`TagInputRelation` は候補生成と候補クリックによる input 反映、`umiFindTreeEve` は `onRegistedFile -> Refresh()` による新規 tag tree 反映まで MainWindow 経由で確認した。`MainWindowWebViewSkinIntegrationTests` は 45/45、`WhiteBrowserSkinApiServiceTests | WhiteBrowserSkinRuntimeBridgeIntegrationTests | WhiteBrowserSkinCompatScriptIntegrationTests | WhiteBrowserSkinEncodingNormalizerTests | WhiteBrowserSkinRenderCoordinatorTests` は 76/76 を確認した。
- 2026-04-13: `WhiteBrowserSkinMovieDto` に legacy lowercase alias `container` / `video` / `audio` / `extra` / `fileDate` / `comments` / `lenSec` / `offset` も追加し、`Search_table` / `Chappy` のように旧 WB の詳細表示メタへ直接触る skin を MainWindow 実 host でも崩さないようにした。これに合わせて `DefaultSmallWB` / `Chappy` / `Search_table` / `Alpha2` は MainWindow 実 host でも初回サムネ表示と `onUpdateThum` 差分更新を確認した。
- 2026-04-13: compat runtime は tag mutation の戻りで `movieInfoCache` / `visibleItemsCache` も同期更新するようにした。`wb.addTag` / `wb.flipTag` 後に `wb.getInfo(id).tags` が古いまま残らないことを compat 統合テストで固定した。
- 2026-04-13: MainWindow 実 host の `umiFindTreeEve` を `onRegistedFile` だけでなく、`onModifyTags` / `onModifyPath` / `onRemoveFile` の tree 系 callback まで押し上げた。`fresh-tag` の tag tree 反映、`fresh` folder 反映、`series-a` 削除を MainWindow 経由で確認した。
- 2026-04-19: MainWindow 実 host の `umiFindTreeEve` で、`onModifyTags(77, ["series-a","sample","fresh-tag"])` 後に bare な `Refresh()` を流しても `fresh-tag` を tag tree へ反映でき、`footer` の `ClearCache` も 1 回だけ維持することを focused で確認した。callback 更新から tree 反映までの最短経路も MainWindow 正本で押さえた。
- 2026-04-19: MainWindow 実 host の `umiFindTreeEve` で、`onModifyPath(77, "E:\\fresh\\beta.mp4") -> Refresh()` で folder tree に `fresh` を反映し、`onRemoveFile(77) -> Refresh()` で `series-a` を含む tag tree を消せることを focused で確認した。path/remove の直反映も MainWindow 正本で押さえた。
- 2026-04-13: `TagInputRelation` は MainWindow 実 host でも `Include` / `Save` から `addTag` 永続化反映まで確認した。`onModifyTags` の厳密 callback 観測は runtime bridge 層を正本にしつつ、MainWindow 側では入力欄クリア、`wb.getInfo(77).tags` 更新、DB の tag 永続化まで確認して受け入れを安定化した。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも `Save` 後に `onExtensionUpdated` を再実行すると、入力欄を空のまま候補を重複なく再生成できることを focused で確認した。runtime bridge 側の再候補生成正本に対して、MainWindow 側の受け入れも揃えた。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも `ButtonGet()` で候補 4 件へ拡張した後に `onExtensionUpdated` を再実行すると、入力欄を空のまま初期候補 3 件へ重複なく戻せることを focused で確認した。Get 後の再候補生成境界も MainWindow 正本で揃えた。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも `Save` 後に `onClearAll` / `onSkinLeave` を挟んで `onExtensionUpdated` を再実行すると、入力欄を空のまま候補を重複なく再生成できることを focused 2 件通過で確認した。Save terminal 後の再候補生成も MainWindow 正本で揃った。
- 2026-04-13: `TagInputRelation` は MainWindow 実 host でも `Get` による候補拡張まで確認した。related movie を 24 件流した状態で、初期候補に出ていない `tag-24` が `ButtonGet()` 後に追加され、続く候補クリックで input 反映できるところまで固定した。MainWindow / compat / runtime bridge / normalizer / render coordinator の広め回帰は 103/103 pass。
- 2026-04-14: compat runtime に `wb.thumSetting(...)` の最小 shim を追加した。`Search_table` の `changeThum()` 初期化で未実装例外が出ていた穴を埋め、MainWindow 実 host で `wb.find("Alpha")` 後に `SearchKeyword` / `wb.getFindInfo().find` / DOM が揃って 1 件へ絞り込まれることを確認した。`Search_table` は差分サムネ更新に加えて、検索 skin としての find 再描画受け入れまで前進した。広め回帰は 104/104 pass。
- 2026-04-14: `Chappy` も MainWindow 実 host で `wb.find("Alpha")` 後の受け入れを追加した。`SearchKeyword` / `wb.getFindInfo().find` / DOM が揃って 1 件へ絞り込まれることを確認し、build 出力 skin の検索系は `Search_table` に続いて `Chappy` も find 再描画まで前進した。MainWindow / compat / runtime bridge / normalizer / render coordinator の広め回帰は 105/105 pass。
- 2026-04-14: `Alpha2` も MainWindow 実 host で `wb.find("Alpha")` 後の受け入れを追加した。`Alpha2` は title id ではなく `thum77 / thum91` のような card 単位 DOM で観測する形へ寄せ、`SearchKeyword` / `wb.getFindInfo().find` / DOM が揃って 1 件へ絞り込まれることを確認した。build 出力 skin の検索系は `Search_table` / `Chappy` / `Alpha2` まで find 再描画を固定し、広め回帰は 106/106 pass。
- 2026-04-14: `DefaultSmallWB` も MainWindow 実 host で `wb.find("Alpha")` 後の受け入れを追加した。`SearchKeyword` / `FilteredMovieRecs` / DOM が揃って 1 件へ絞り込まれることを確認し、build 出力 skin 4 本 (`DefaultSmallWB` / `Search_table` / `Chappy` / `Alpha2`) はすべて MainWindow 実 host で find 再描画まで確認済みになった。広め回帰は 107/107 pass。
- 2026-04-14: `Search_table` は MainWindow 実 host で `wb.sort("ファイル名(降順)")` の受け入れも追加した。`MainVM.DbInfo.Sort == "13"`、`FilteredMovieRecs` が `84 -> 91 -> 77`、DOM の title id 順が `title84 -> title91 -> title77` へ更新されることを確認し、検索 skin として `find` に続いて `sort` まで実 host で受け入れ済みになった。広め回帰は 108/108 pass。
- 2026-04-14: `Search_table` は MainWindow 実 host で `wb.addFilter("idol")` の受け入れも追加した。`MainVM.DbInfo.SearchKeyword == "!tag:idol"`、`FilteredMovieRecs` が `84 -> 77`、`wb.getFindInfo().filter == ["idol"]`、DOM が `title84 / title77` の 2 件へ揃うことを確認し、検索 skin として `find` / `sort` に続いて `addFilter` まで実 host で受け入れ済みになった。
- 2026-04-15: `Search_table` は MainWindow 実 host で `wb.addFilter("idol") -> wb.addFilter("sample") -> wb.removeFilter("sample")` の受け入れも確認した。`SearchKeyword` が `!tag:idol` に戻り、`FilteredMovieRecs` と DOM が `84 -> 77` の 2 件へ揃うことを確認し、tag filter の追加だけでなく除去も実 host で固定した。
- 2026-04-15: `Search_table` は MainWindow 実 host で `wb.clearFilter()` の受け入れも確認した。`wb.addFilter("idol") -> wb.addFilter("sample") -> wb.clearFilter()` 後、`SearchKeyword` と `wb.getFindInfo().filter` が空へ戻り、`FilteredMovieRecs` と DOM が既定順 `91 -> 84 -> 77` の 3 件へ復帰することを確認した。
- 2026-04-14: compat runtime の `wb.getFindInfo()` は、`onUpdate` で入った新しい検索状態を古い `getFindInfo` 応答で上書きしないようにした。cache epoch を持たせ、古い非同期応答は破棄することで、skin 側の同期参照が直前の `onUpdate.findInfo` に追従し続けることを compat 統合テストで固定した。
- 2026-04-14: `Search_table` は MainWindow 実 host で `wb.addWhere("score >= 18")` の受け入れも追加した。`SearchKeyword` を汚さず `wb.getFindInfo().where == "score >= 18"` と `#where == "{score >= 18}"` が同期し、DOM が `title91 / title84` の 2 件へ揃うことを確認した。Search_table の本流である `where` 条件も実 host で受け入れ済みになった。
- 2026-04-14: `Alpha2` は MainWindow 実 host で `wb.sort("ファイル名(降順)")` の受け入れも追加した。`MainVM.DbInfo.Sort == "13"`、`FilteredMovieRecs` が `84 -> 91 -> 77`、card DOM 順が `thum84 -> thum91 -> thum77` へ更新され、`wb.getFindInfo().sort[0] == "ファイル名(降順)"` も維持されることを確認した。
- 2026-04-14: `Search_table` は MainWindow 実 host で `wb.onSkinEnter()` の profile 復元も確認した。`lastsort = "ファイル名(降順)"` と `lastwhere = "score >= 18"` を書き戻した状態で再入すると、`MainVM.DbInfo.Sort == "13"`、`wb.getFindInfo().sort[0] == "ファイル名(降順)"`、`wb.getFindInfo().where == "score >= 18"`、`#where == "{score >= 18}"`、DOM が `title84 / title91` に揃うことを確認した。Search_table の起動時復元も実 host で受け入れ済みになった。
- 2026-04-14: `Chappy` は MainWindow 実 host で `wb.addFilter("idol")` の受け入れも追加した。`MainVM.DbInfo.SearchKeyword == "!tag:idol"`、`FilteredMovieRecs` が `84 -> 77`、`wb.getFindInfo().filter == ["idol"]`、card DOM が `thum84 -> thum77` へ揃うことを確認し、一覧 skin として tag filter 再描画も実 host で固定した。
- 2026-04-15: `Chappy` は MainWindow 実 host で `wb.addFilter("idol") -> wb.addFilter("sample") -> wb.removeFilter("sample")` の受け入れも確認した。`SearchKeyword` が `!tag:idol` に戻り、`FilteredMovieRecs` と card DOM が `thum84 -> thum77` へ揃うことを確認し、tag filter の除去も実 host で固定した。
- 2026-04-15: `Chappy` は MainWindow 実 host で `wb.clearFilter()` の受け入れも確認した。`wb.addFilter("idol") -> wb.addFilter("sample") -> wb.clearFilter()` 後、`SearchKeyword` と `wb.getFindInfo().filter` が空へ戻り、`FilteredMovieRecs` と card DOM が既定順 `thum91 -> thum84 -> thum77` の 3 件へ復帰することを確認した。
- 2026-04-14: `Chappy` は MainWindow 実 host で `wb.addWhere("score >= 18")` の受け入れも追加した。`SearchKeyword` を汚さず `wb.getFindInfo().where == "score >= 18"` を維持しつつ、card DOM が `thum91 -> thum84` の 2 件へ揃うことを確認し、一覧 skin として where 条件の再描画も実 host で固定した。
- 2026-04-14: `Chappy` は MainWindow 実 host で `wb.onSkinEnter()` による設定 profile 復元も確認した。`thumb_size = "240x180x4x2"`、`detail_size = "120x90x6x3"`、`auto_focus = "1234"` を書き戻し、`ConfigLoadFlg = false` で再入すると、`stg_form_*` と `SkinConf.*` の両方が指定値へ揃うことを確認した。Chappy の起動時設定復元も実 host で受け入れ済みになった。
- 2026-04-14: `Chappy` は MainWindow 実 host で `wb.sort("ファイル名(降順)")` の受け入れも追加した。`MainVM.DbInfo.Sort == "13"`、`FilteredMovieRecs` が `84 -> 91 -> 77`、card DOM 順が `thum84 -> thum91 -> thum77` へ更新されることを確認し、最小ではない一覧 skin でも共通再描画が sort に追従することを固定した。
- 2026-04-14: `Alpha2` は MainWindow 実 host で `wb.addFilter("idol")` の受け入れも追加した。`MainVM.DbInfo.SearchKeyword == "!tag:idol"`、`FilteredMovieRecs` が `84 -> 77`、card DOM が `thum84 -> thum77` へ揃うことを確認し、旧同期前提が濃い skin でも tag filter 再描画が本体検索と同期することを固定した。
- 2026-04-15: `Alpha2` は MainWindow 実 host で `wb.addFilter("idol") -> wb.addFilter("sample") -> wb.removeFilter("sample")` の受け入れも確認した。`SearchKeyword` が `!tag:idol` に戻り、`FilteredMovieRecs` と card DOM が `thum84 -> thum77` へ揃うことを確認し、旧同期前提が濃い skin でも tag filter の除去を安定して扱えるようにした。
- 2026-04-15: `Alpha2` は MainWindow 実 host で `wb.clearFilter()` の受け入れも確認した。`wb.addFilter("idol") -> wb.addFilter("sample") -> wb.clearFilter()` 後、`SearchKeyword` と `wb.getFindInfo().filter` が空へ戻り、`FilteredMovieRecs` と card DOM が既定順 `thum91 -> thum84 -> thum77` の 3 件へ復帰することを確認した。
- 2026-04-14: `Alpha2` は MainWindow 実 host で `wb.addWhere("score >= 18")` の受け入れも追加した。`SearchKeyword` を空のまま保ちつつ `wb.getFindInfo().where == "score >= 18"` が効き、card DOM が `thum91 -> thum84` の 2 件へ揃うことを確認した。旧同期前提が濃い skin でも where 条件の再描画が本体検索へ追従することを固定した。
- 2026-04-14: `Alpha2` は MainWindow 実 host で `wb.onSkinEnter()` による layout profile 復元も確認した。`setting = packConfigStr(default_setting)` と `current_layout = packConfigStr(default_layout_table[2])` を書き戻し、`ConfigLoaded = false` で再入すると、`current_layout["thum_size"] == "200x150"`、`current_layout["layout_name"] == default_layout_table[2]["layout_name"]`、`current_layout["info_item"]` が `score` を含む状態へ揃い、DOM も `thum77 / thum91` のまま維持されることを確認した。Alpha2 の起動時 layout 復元も実 host で受け入れ済みになった。
- 2026-04-14: `DefaultSmallWB` は MainWindow 実 host で `wb.addFilter("idol")` の受け入れも追加した。`MainVM.DbInfo.SearchKeyword == "!tag:idol"`、`FilteredMovieRecs` が `84 -> 77`、DOM が `title84 -> title77` へ揃うことを確認し、最小一覧 skin でも tag filter 再描画が本体検索と同期することを固定した。
- 2026-04-15: `DefaultSmallWB` は MainWindow 実 host で `wb.addFilter("idol") -> wb.addFilter("sample") -> wb.removeFilter("sample")` の受け入れも確認した。`SearchKeyword` が `!tag:idol` に戻り、`FilteredMovieRecs` と DOM が `title84 -> title77` へ揃うことを確認し、最小一覧 skin でも tag filter の除去を安定して扱えるようにした。
- 2026-04-15: `DefaultSmallWB` は MainWindow 実 host で `wb.clearFilter()` の受け入れも確認した。`wb.addFilter("idol") -> wb.addFilter("sample") -> wb.clearFilter()` 後、`SearchKeyword` と `wb.getFindInfo().filter` が空へ戻り、`FilteredMovieRecs` と DOM が既定順 `title91 -> title84 -> title77` の 3 件へ復帰することを確認した。
- 2026-04-14: `DefaultSmallWB` は MainWindow 実 host で `wb.addWhere("score >= 18")` の受け入れも追加した。`SearchKeyword` を空のまま保ちつつ `wb.getFindInfo().where == "score >= 18"` が効き、DOM が `title91 -> title84` の 2 件へ揃うことを確認した。最小一覧 skin でも where 条件の再描画が本体検索へ追従することを固定した。
- 2026-04-14: `DefaultSmallWB` は MainWindow 実 host で `wb.sort("ファイル名(降順)")` の受け入れも追加した。`MainVM.DbInfo.Sort == "13"`、`FilteredMovieRecs` が `84 -> 91 -> 77`、DOM の title id 順が `title84 -> title91 -> title77` へ更新され、最小 skin でも一覧再描画が sort に素直に追従することを確認した。
- 2026-04-14: `DefaultSmallWB` は MainWindow 実 host で `seamless-scroll : 2` の追記も確認した。`#view` を scroll container として `200 -> 201 items` へ追記され、`title201 == "Movie201.mp4"`、`score201 == "1"` を確認した。DefaultSmallWB の価値ある次手である append 互換も実 host で受け入れ済みになった。
- 2026-04-15: `DefaultSmallWB` の build 出力実体は `onSkinEnter` / profile 読み戻しを持たず、設定は `#config` の静的値だけであることを確認した。そこで MainWindow 実 host では `wb.onSkinEnter()` 再実行が no-op として一覧を壊さないことを受け入れ条件に寄せ、DOM と `FilteredMovieRecs` が既定順 `title91 -> title84 -> title77` のまま維持されることを固定した。
- 2026-04-15: `skin` 切り替え高速化の保存系土台として、`UpdateSort()` の MainWindow 受け入れも追加した。通常時は単一ライター persister 経由で `system.sort` へ保存でき、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は queue 拒否を検知して fallback 直書きへ戻せることを固定し、`sort` 保存の経路切り替えが安全であることを確認した。関連 targeted (`MainWindowSearchBoxEnterTests | WhiteBrowserSkinStatePersisterTests | WhiteBrowserSkinProfileValueCacheTests | WhiteBrowserSkinCatalogServiceTests`) は 27/27 pass。
- 2026-04-15: `skin` 切り替え高速化の保存系土台として、`UpdateSkin()` の MainWindow 受け入れも追加した。通常時は単一ライター persister 経由で `system.skin` と外部 skin の `profile.LastUpperTab` を保存でき、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は queue 拒否を検知して両方とも fallback 直書きへ戻せることを固定した。`UpdateSkin()` 側で queue 拒否時に何も残さず落ちる穴は `WhiteBrowserSkinOrchestrator` と `MainWindow.SkinPersistence` の fallback 補完で解消し、関連 targeted (`PersistCurrentSkinState | UpdateSkin_外部skinを単一ライター経由でsystemとprofileへ保存できる | UpdateSkin_skinPersister入力完了後はfallback直書きでsystemとprofileへ保存できる`) は 4/4 pass。
- 2026-04-15: `skin` 切り替え高速化の保存系土台として、`ApplySkinByName("DefaultGrid", persistToCurrentDb: true)` の MainWindow 受け入れも追加した。built-in skin の `system.skin` は通常時は単一ライター persister、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は fallback 直書きへ戻せることを固定し、system-only 分岐も MainWindow 側で守った。関連 targeted (`ApplySkinByName_組み込みskin*`) は 2/2 pass。
- 2026-04-15: `skin` 切り替え高速化の保存系土台として、`Watcher` の `SaveEverythingLastSyncUtc(...)` の MainWindow 受け入れも追加した。通常時は単一ライター persister 経由で `system.everything_last_sync_utc_*` へ保存でき、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後も queue 拒否を検知して fallback 直書きへ戻せることを固定した。関連 targeted (`UpdateSort* | UpdateSkin* | SaveEverythingLastSyncUtc*`) は 6/6 pass。
- 2026-04-15: `skin` 切り替え高速化の保存系土台として、`MenuBtnSettings_Click` 後段の個別設定保存を `PersistDbSettingsValues(...)` へ集約した。`thum` / `bookmark` / `keepHistory` / `playerPrg` / `playerParam` は通常時は単一ライター persister、`BeginWhiteBrowserSkinStatePersisterShutdown()` 後は fallback 直書きへ戻せることを MainWindow 受け入れで確認し、関連 targeted (`PersistDbSettingsValues*`) を追加した。
- 2026-04-15: `refresh` 起点一本化の本線として、`ApplySkinByName(...)` 経由の外部 skin 切替は MainWindow 実 host でも `dbinfo-Skin` を起点に 1 回だけ apply されることを統合テストで固定した。`ApplySkinByName` 直後に別の明示 queue を積まず、`DbInfo.Skin` の PropertyChanged を正本にしていることを見える形にした。関連 targeted (`ApplySkinByName経由の外部skin_refresh起点はdbinfo_Skinへ一本化される | ApplySkinByName経由でも外部skin_host表示へ切替できる | skin切替競合でも古いrefresh完了で表示が巻き戻らない`) は 3/3 pass。
- 2026-04-15: `refresh` 起点一本化の本線として、`BootNewDb(...)` 中に連続する `DBFullPath / Skin / ThumbFolder` の PropertyChanged は batch 化し、外部 skin refresh は最後に `dbinfo-DBFullPath` へ 1 回だけ流す形へ整理した。旧 `boot-new-db` 特例 reason は外し、MainWindow 実 host 統合テストで `Prepare == 1`、`apply == 1`、reason が `dbinfo-DBFullPath` であることを固定した。関連 targeted (`BootNewDb経由の外部skin_refresh起点はdbinfo_DBFullPathへ一本化される | ApplySkinByName経由の外部skin_refresh起点はdbinfo_Skinへ一本化される | 外部skin表示中のDB切替でもhost表示を維持して再準備できる | ApplySkinByName経由でも外部skin_host表示へ切替できる | skin切替競合でも古いrefresh完了で表示が巻き戻らない`) は 5/5 pass。
- 2026-04-15: `MainWindow_ContentRendered` の `StartupAutoOpen` 経路でも、`TrySwitchMainDb(...) -> BootNewDb(...)` を通った外部 skin refresh が `dbinfo-DBFullPath` に収束することを MainWindow 実 host 統合テストで固定した。起動復元だけ別 reason に逃げず、`Prepare == 1`、`apply == 1` を維持している。関連 targeted (`StartupAutoOpen経由の外部skin_refresh起点もdbinfo_DBFullPathへ一本化される` を含む refresh 本線 6 件) は 6/6 pass。
- 2026-04-15: `ThumbFolder` 変更は `dbinfo-ThumbFolder` を独立 reason として維持する判断を MainWindow 実 host 統合テストで固定した。外部 skin 表示中にサムネ root を切り替えると `Prepare` が 1 回だけ追加で走り、reason は `dbinfo-ThumbFolder` である。これにより、`DBFullPath` 系は一本化しつつ、thumb root 実体変更だけは必要な独立 refresh 起点として残した。
- 2026-04-15: `WhiteBrowserSkinCatalogService.Load(...)` の cache telemetry を追加した。`debug-runtime.log` に `skin-catalog` の hit / miss を残し、focused test で `same root` 再読込は `hit` だけ増え、html 更新時だけ `miss` が増えることを確認した。catalog 再走査削減が効いているかを次段の runtime 観測で追いやすくした。
- 2026-04-16: `WhiteBrowserSkinCatalogService.BuildCatalogSignature(...)` も `skin-catalog` ログへ `directories` と `elapsed_ms` を残すようにした。focused test では signature build 回数、最後に走った directory 数、経過時間 telemetry を取得できることを確認し、cache 判定より前に掛かるコストも見えるようにした。
- 2026-04-16: catalog snapshot 作成も、前回 cache と一致するディレクトリは html metadata を再利用するようにした。focused test では `same root` 再読込で signature 側の `reused` が増え、一部更新時は未変更ディレクトリだけ再利用されることを確認した。
- 2026-04-16: `WhiteBrowserSkinCatalogService` は miss 時に signature 用 metadata と実 load を同じ snapshot で共有する形へ寄せた。catalog 再読込時のディレクトリ総なめを 2 回繰り返さず、signature と本体読込の重複仕事を減らしている。
- 2026-04-16: `WhiteBrowserSkinCatalogService.LoadCore(...)` も `skin-catalog` ログへ `items` / `external` / `elapsed_ms` を残すようにした。focused test では load core 回数、最後に生成した external skin 数、経過時間 telemetry を取得できることを確認し、signature 計算と実定義生成のどちらが重いかも分けて追えるようにした。
- 2026-04-16: `catalog load core built` にも `root` を出すようにし、`signature built / cache hit / miss / load core built` を同じ skin root 軸で読み合わせやすくした。
- 2026-04-16: `WhiteBrowserSkinCatalogService.LoadCore(...)` は miss 時でも、前回 snapshot と一致する外部 skin については `WhiteBrowserSkinDefinition` を参照再利用するようにした。focused test では「2件中1件だけ更新」の再読込で未変更側が同一参照、更新側だけ差し替わり、`reused` 件数 telemetry も取得できることを確認した。
- 2026-04-16: `LoadCore(...)` の定義再利用判定は html metadata 基準へ寄せ、CSS / JS / 画像など非 HTML 資産だけ更新した時も `WhiteBrowserSkinDefinition` を参照再利用できるようにした。focused test で「asset 更新だけなら miss でも definition は再利用される」ことを確認した。
- 2026-04-16: `WhiteBrowserSkinOrchestrator` 経由の一覧再取得でも、未変更 skin 定義が参照再利用されることを focused test で確認した。MainWindow 相当の利用経路でも、html を触っていない skin まで毎回作り直さない。
- 2026-04-16: `debug-runtime.log` の全カテゴリ行へ共通連番を付け、`skin-webview / skin-catalog / skin-db` を同じファイル上で時系列に追い返しやすくした。`DebugRuntimeLogTests` で行フォーマットも固定した。
- 2026-04-16: `debug-runtime.log` の1行性を守るため、カテゴリ名とメッセージ中の改行・タブは空白化するようにした。複数行メッセージでも連番付きの観測が崩れないことを `DebugRuntimeLogTests` で固定した。
- 2026-04-16: `MainWindow.SkinPersistence` では `persist queued` と `system/persist fallback applied` も `skin-db` ログへ残すようにした。これで `skin-webview` の refresh batch、`skin-catalog` の hit/miss・signature build、`skin-db` の enqueue / fallback を同じ `debug-runtime.log` で時系列に読み合わせできる。
- 2026-04-17: `Search_table` は MainWindow 実 host で `wb.scrollTo(60)` の受け入れも確認した。`#view` をスクロールコンテナとして `scrollTop > 0` まで移動でき、build 出力 skin 側でも `scroll-id` 非依存の代表ケースが固定できた。
- 2026-04-17: `Search_table` は MainWindow 実 host で `wb.selectThum(77, true)` と `wb.selectThum(84, true)` の受け入れも確認した。`wb.getSelectThums()` と WPF 側の複数選択が `77 / 84` で揃い、focus は `77` のまま維持されることを固定した。
- 2026-04-17: `wblib-compat.js` は `onUpdate` 応答の `select == 1` を internal selected cache へ同期するようにした。これで `Search_table` のように初回描画で `thum_select` が付いている skin でも、最初の `wb.selectThum(id, false)` で `onSetSelect(false)` が確実に返り、stale class が残りにくくなった。
- 2026-04-17: `wblib-compat.js` の selected cache 同期は、増分 `onUpdate` でも `select:0` を明示的に削除するようにした。これで部分更新後に `runtimeState.selectedIds` が stale のまま残り、`getSelectThums()` や `clearAll` 時の false callback がずれる余地を減らした。
- 2026-04-17: `wblib-compat.js` の `onUpdate` 後は host 側 `getSelectThums()` を再取得して selected cache を authoritative state へ再同期するようにした。payload に出てこない旧選択行がいても、増分更新後に `77:false` のような解除 callback を返して stale を残さないことを compat 単体で確認した。
- 2026-04-17: `wblib-compat.js` の `getSelectThums()` 再同期には request serial / epoch ガードも追加した。古い `onUpdate` や古い `selectThum()` 由来の選択要求が後着しても、新しい host 選択状態を巻き戻さないことを compat 単体で確認した。
- 2026-04-17: `wblib-compat.js` の `getSelectThums()` は、「compat 内部の authoritative 再同期」と「skin 側が await する API」を分離した。内部再同期だけ request serial / epoch ガードで保護し、skin 側 `await wb.getSelectThums()` は後着した host 応答そのものを受け取れることを compat 単体で確認した。
- 2026-04-17: `wblib-compat.js` は `onSetFocus(false)` 後の最小視覚同期も持つようにした。custom `onSetFocus` が false を無視したり outer class を戻し切らない skin でも、旧 focus 行を `thum / cthum / img_thum / cimg` 側へ戻せる。
- 2026-04-17: `wblib-compat.js` の既定 `onSetSelect` fallback は、focus 中の `select(false)` でも focus class を落とし過ぎないようにした。`onSetSelect` 未実装 skin でも、`thum_focus thum_select -> thum_focus` のように focus 表示を維持できることを compat 単体で確認済みである。
- 2026-04-17: `wblib-compat.js` の既定 `onSetFocus` fallback は gain 側も持つようにした。`onSetFocus` 未実装 skin でも `thum_focus / img_focus / title_focus` 相当の最小 focus 表示を compat 単体で同期できる。
- 2026-04-17: `wblib-compat.js` の既定 `onSetFocus` fallback は、compact 行で focus を外した後も `cthum` を base class として維持するようにした。`select(true) -> focus(true) -> focus(false)` の順でも `cthum thum_select` に戻り、compact 行が `thum_select` や `thum` へ退化しないことを compat 単体で確認した。
- 2026-04-17: `Search_table` は compat 単体でも、実 skin と同等の custom `onSetFocus / onSetSelect` 下で `focusThum(77) -> selectThum(77, true) -> selectThum(84, true) -> focusThum(84) -> selectThum(84, false)` を確認した。最終的に `focusedId == 0`、`selectedIds == []`、`thum77 / thum84 / img77 / img84 / title77 / title84` はすべて plain class へ戻り、Search_table 固有 callback 下でも stale class を残さない。
- 2026-04-17: `Search_table` は MainWindow 実 host でも、focus 行 `84` に対する `selectThum(84, false)` で host 側の focus と選択がまとめて空へ戻ることを確認した。`thum77 / thum84 / img77 / img84 / title77 / title84` も `thum / img_thum / title_thum` の plain 表示へ戻り、見た目残差は再現しなかった。
- 2026-04-17: `DefaultSmallWB` は MainWindow 実 host で `wb.scrollTo(60)` の受け入れも確認した。`#view` をスクロールコンテナとして `scrollTop > 0` まで移動でき、minimal list skin でも `scroll-id : view` の操作系が通ることを固定した。
- 2026-04-17: `DefaultSmallWB` は MainWindow 実 host で `wb.selectThum(77, true)` と `wb.selectThum(84, true)` の受け入れも確認した。`wb.getSelectThums()` と WPF 側の複数選択が `77 / 84` で揃い、focus は `77` のまま維持されることを固定した。
- 2026-04-17: `wblib-compat.js` は `onSetSelect` 未実装 skin 向けに、`#thum{id}` へ `thum_select` と `data-imm-selected` を同期する最小視覚 fallback を追加した。`DefaultSmallWB` では MainWindow 実 host と compat 単体の両方で、選択同期だけでなく class 反映も確認済みになった。
- 2026-04-17: `wblib-compat.js` の `onSetSelect` 既定 fallback は compact 系 row でも base class を保持するように補正した。`cthum` 行で `select(true) -> select(false)` を行っても plain が `thum` へ退化せず、`cthum` へ戻ることを compat 単体で確認した。
- 2026-04-17: `wblib-compat.js` の `onSetFocus` 既定 fallback も compact selected 行に追従させた。`cthum` 行で `select(true) -> focus(true) -> focus(false)` を行っても `cthum thum_select` を保ち、focus だけ外れることを compat 単体で確認した。
- 2026-04-17: `DefaultSmallWB` は MainWindow 実 host で `wb.focusThum(77) -> wb.selectThum(77, true) -> wb.selectThum(84, true) -> wb.focusThum(84)` の受け入れも確認した。host 実体では選択集合が `84` へ縮み、旧 row は `thum` へ戻り、新 focus 行は `thum_focus thum_select / img_focus` を維持する。つまり DefaultSmallWB も「focus 行は focus 優先だが stale class は残さない」側で固定できた。
- 2026-04-17: `DefaultSmallWB` は MainWindow 実 host で `wb.focusThum(77) -> wb.selectThum(77, true) -> wb.selectThum(77, false)` も確認した。host 実体では選択集合は空へ戻り、focus も同時に解除され、row は `thum / img_thum` の plain 表示へ戻る。
- 2026-04-17: `DefaultSmallWB` は MainWindow 実 host で、focused + selected な row に `onUpdateThum("db-main:84", "...")` を返しても `thum_focus thum_select / img_focus` を維持したまま thumb src だけ差し替わることを確認した。`onUpdateThum` 差分更新で focus/select 表示を壊さない。
- 2026-04-17: `Alpha2` は MainWindow 実 host で、focused + selected な card に `onUpdateThum("db-main:84", "...")` を返しても row 側は `cthum thum_select` を保ち、画像側は `cimg_focus` を維持したまま thumb src だけ差し替わることを確認した。card 系 skin でも `onUpdateThum` 差分更新で選択表示と画像 focus 強調を壊さない。
- 2026-04-17: `Alpha2` は MainWindow 実 host で、focused + selected な card に `onModifyTags(84, ["idol","fresh-tag"])` を返しても `tag84` の表示だけを差し替えつつ、row 側は `cthum thum_select`、画像側は `cimg_focus` を維持できることを確認した。tag 差分更新でも focus/select の見た目を壊さない。
- 2026-04-17: `Chappy` は MainWindow 実 host で、focused + selected な row に `onUpdateThum("db-main:84", "...")` を返しても `thum_focus thum_select / img_thum_focus` を維持したまま thumb src だけ差し替わることを確認した。build 出力 card/list 系でも `onUpdateThum` 差分更新で focus/select 表示を壊さない。
- 2026-04-17: `Chappy` は MainWindow 実 host で、focused + selected な row に `onModifyTags(84, ["idol","fresh-tag"])` を返しても `tag84 / tags_disp84` の表示だけを差し替えつつ、`thum_focus thum_select / img_thum_focus` を維持できることを確認した。card/list 系の tag 差分更新でも focus/select 表示を壊さない。
- 2026-04-17: `Search_table` は MainWindow 実 host で、focused + selected な row に `onUpdateThum("db-main:84", "...")` を返しても `thum_select / img_focus / title_focus` を維持したまま thumb src だけ差し替わることを確認した。table/list 系の自前 `onUpdateThum` を持つ skin でも差分更新で focus/select 表示を壊さない。
- 2026-04-19: `Search_table` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("DefaultSmallWB")` を呼んでも差分 thumb URL を次 skin の `img84.src` へ持ち越さず、既定サムネ表示へ戻ることを確認した。差分サムネ更新後の success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Search_table` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `img84.src` は既定サムネ表示へ戻り更新済み thumb URL を持ち越さないことを focused 3 件通過で確認した。MainWindow 側でも thumb 差分更新後 clear 終端 success 境界を 1 本追加で固定できた。
- 2026-04-19: `Search_table` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と更新済み `img84.src`、`thum_select / img_focus / title_focus` を維持することを確認した。差分サムネ更新後の failure 境界も no-op として扱える。
- 2026-04-19: `Chappy` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("DefaultSmallWB")` を呼んでも差分 thumb URL を次 skin の `img84.src` へ持ち越さず、既定サムネ表示へ戻ることを確認した。card/list 系 skin の差分サムネ更新後 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `img84.src` は既定サムネ表示へ戻り更新済み thumb URL を持ち越さないことを確認した。card/list 系 skin の差分サムネ更新後 clear 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-19: `Chappy` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と更新済み `img84.src`、`thum_focus thum_select / img_thum_focus` を維持することを確認した。card/list 系 skin の差分サムネ更新後 failure 境界も no-op として扱える。
- 2026-04-17: `Search_table` は MainWindow 実 host で、focused + selected な row に `onModifyTags(84, ["idol","fresh-tag"])` を返しても `tag84` の表示だけを差し替えつつ、`thum_select / img_focus / title_focus` を維持できることを確認した。table/list 系の tag 差分更新でも focus/select 表示を壊さない。
- 2026-04-18: `DefaultSmallWB` は MainWindow 実 host で、focused + selected な row に `onModifyTags(84, ["idol","fresh-tag"])` を返しても `tag84` の表示だけを差し替えつつ、`thum_focus thum_select / img_focus` を維持できることを確認した。minimal grid 系の tag 差分更新でも focus/select 表示を壊さない。
- 2026-04-19: `DefaultSmallWB` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("Search_table")` を呼んでも差分 thumb URL を次 skin の `img84.src` へ持ち越さず、既定サムネ表示へ戻ることを確認した。minimal list skin の差分サムネ更新後 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("Search_table")` しても、次 skin の `img84.src` は既定サムネ表示へ戻り更新済み thumb URL を持ち越さないことを確認した。minimal list skin の差分サムネ更新後 clear 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-19: `DefaultSmallWB` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と更新済み `img84.src`、`thum_focus thum_select / img_focus` を維持することを確認した。minimal list skin の差分サムネ更新後 failure 境界も no-op として扱える。
- 2026-04-19: runtime bridge 実 host でも `DefaultSmallWB / Chappy / Search_table / Alpha2` の build 出力 skin 4 本に対して `onUpdateThum(77, "...")` 後に `wb.changeSkin(...)` 成功時は、次 skin の `img77.src` を既定サムネ表示へ戻し、差分 URL を持ち越さないことを focused 8 件通過で確認した。build 出力 skin の差分サムネ更新後 success 境界も bridge 正本で揃った。
- 2026-04-19: runtime bridge 実 host でも `DefaultSmallWB / Chappy / Search_table / Alpha2` の build 出力 skin 4 本に対して `onUpdateThum(77, "...")` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin 名と更新済み `img77.src` を維持することを focused 8 件通過で確認した。build 出力 skin の差分サムネ更新後 failure 境界も bridge 正本で揃った。
- 2026-04-18: runtime bridge 実 host でも `DefaultSmallWB / Chappy / Search_table / Alpha2` の build 出力 skin 4 本に対して `onModifyTags(77, ["idol","fresh-tag"])` を流し、`tag77` の表示が `series-a` から `fresh-tag` へ差し替わることを focused 4 件通過で確認した。一覧系 skin の tag 差分更新は MainWindow 実 host だけでなく bridge 正本側でも面で揃った。
- 2026-04-17: compat 単体でも、compact row に対する `handleSkinLeave()` は `focus / select` をまとめて落とし、`cthum` の base class と `data-imm-selected=0` へ戻ることを focused test で固定した。`onSkinLeave / onClearAll` の lifecycle 経路でも compact row が `thum` 側へ退化しない。
- 2026-04-17: `Alpha2` は MainWindow 実 host で `wb.scrollTo(60)` の受け入れも確認した。`#view` をスクロールコンテナとして `scrollTop > 0` まで移動でき、旧同期前提が濃い card skin でも `scroll-id : view` の操作系が通ることを固定した。
- 2026-04-17: `Chappy` は MainWindow 実 host で `wb.scrollTo(60)` の受け入れも確認した。`#view` をスクロールコンテナとして `scrollTop > 0` まで移動でき、build 出力一覧 skin の card 系でも `scroll-id : view` の操作系が通ることを固定した。
- 2026-04-17: `Chappy` は MainWindow 実 host で `wb.selectThum(77, true)` と `wb.selectThum(84, true)` の受け入れも確認した。`wb.getSelectThums()` と WPF 側の複数選択が `77 / 84` で揃い、focus は `77` のまま維持され、focused row は `thum_focus` に加えて `thum_select` も持つ形で focus/select 強調が共存することを固定した。
- 2026-04-17: `Chappy` は MainWindow 実 host で `wb.focusThum(77) -> wb.focusThum(84)` の受け入れも確認した。host 実体では選択集合が `84` へ縮み、旧 focus 行は `thum / img_thum` へ戻り、新 focus 行は `thum_focus / img_thum_focus` を保つことを固定した。
- 2026-04-17: `Chappy` は MainWindow 実 host で、focus 行 `84` に対して `wb.selectThum(84, false)` を返した時は host 側の focus と選択がまとめて空へ戻ることを確認した。互換層はこの実体へ追従し、`thum84 / img84` も plain class へ戻す。
- 2026-04-17: `Chappy` は MainWindow 実 host で `wb.focusThum(77) -> wb.selectThum(77, true) -> wb.selectThum(77, false)` も確認した。host 実体では選択集合は空へ戻り、focus も同時に解除され、row は `thum / img_thum` の plain 表示へ戻る。
- 2026-04-17: `Alpha2` は MainWindow 実 host で `wb.selectThum(77, true)` と `wb.selectThum(84, true)` の受け入れも確認した。`wb.getSelectThums()` と WPF 側の複数選択が `77 / 84` で揃い、focus は `77` のまま維持されるが、outer row は `thum_select` 優先、画像だけが `cimg_focus` を保つ。
- 2026-04-17: `Alpha2` は MainWindow 実 host で `wb.focusThum(77) -> wb.focusThum(84)` の受け入れも確認した。host 実体では選択集合が `84` へ縮み、旧 focus 行は `cthum / cimg` 側へ戻り、新 focus 行だけ focus class を保つことを固定した。
- 2026-04-17: `Alpha2` は MainWindow 実 host で、focus 行 `84` に対して `wb.selectThum(84, false)` を返した時は host 側の focus と選択がまとめて空へ戻ることを確認した。互換層はこの実体へ追従し、`thum84 / img84` も plain class へ戻す。
- 2026-04-17: `Alpha2` は MainWindow 実 host で `wb.focusThum(77) -> wb.selectThum(77, true) -> wb.selectThum(77, false)` も確認した。host 実体では選択集合は空へ戻り、focus も同時に解除され、row は `cthum / cimg` の plain 表示へ戻る。
- 2026-04-17: MainWindow 実 host の最終確認として、`Search_table / DefaultSmallWB / Chappy / Alpha2` の `scrollTo / selectThum / focusThum / focus中解除` を見直した。現時点では 4 skin とも class の違和感や期待値ズレは再現せず、解除意味論は 4 skin で一貫して受け入れ固定できている。
- 2026-04-17: `wblib-compat.js` の `getSelectThums()` は、compat 内部の authoritative 再同期と skin 側 API 呼び出しを分離した。内部再同期だけを serial / epoch ガードで保護し、skin 側 `await wb.getSelectThums()` は後着した host 応答そのものを受け取れることを focused で固定した。
- 2026-04-17: `wblib-compat.js` の `handleClearAll()` / `handleSkinLeave()` は、deselect callback より前に compat の selected cache を空へ寄せるようにした。`onSetSelect(false)` の中で `wb.getSelectThums()` を読んでも stale seed を返さず、clear 系 lifecycle 中の同期読み出しが host 実体に近い状態で揃う。
- 2026-04-17: `wblib-compat.js` の `wb.changeSkin("DefaultSmallWB")` 成功直後は、旧 page 上の compat cache も先に新しい skin 名へ進めるようにした。host 再遷移前でも `await wb.getSkinName()` が新しい skin 名を返せることを focused で固定した。
- 2026-04-17: `TagInputRelation` は runtime bridge 実 host でも、`Save` 後に `onExtensionUpdated` を再実行して候補再生成できることを確認した。入力欄は空のまま維持され、候補リストも空や重複に崩れない。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、`#TagInputRelation -> DefaultSmallWB -> #TagInputRelation` と skin 切替往復した後に、input を空のまま保ちつつ候補 4 件を重複なく再生成できることを確認した。leave / re-enter 後も extension 状態を持ち越さない。
- 2026-04-17: `TagInputRelation` の MainWindow 実 host 再入受け入れをさらに強化し、再入直後は `#input` と `#Selection` が空で、その後 `onExtensionUpdated` を流した時だけ候補 4 件が再生成されることを固定した。終端状態と再生成境界を分けて観測できる。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、`onClearAll` / `onSkinLeave` 直後に input と候補表示を持ち越さず、`#TagInputRelation -> DefaultSmallWB -> #TagInputRelation` の再入で候補 4 件を重複なく戻せることを確認した。終端系 callback 後の extension 状態を持ち越さない。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、script 側から `wb.changeSkin("DefaultSmallWB")` を呼んだ直後に、次 skin へ `#input` / `#Selection` を持ち越さないことを確認した。skin API 経由の切替でも extension 状態を次画面へ漏らさない。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、dirty な input 状態のまま script 側から `wb.changeSkin("#umlFindTreeEve")` を呼んでも、`#input` / `#Selection` を tree / footer 側へ持ち越さないことを確認した。extension 同士の切替でも carry-over を起こさない。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、`Save` 後の dirty state のまま script 側から `wb.changeSkin("#umlFindTreeEve")` を呼んでも、`#input` / `#Selection` を tree / footer 側へ持ち越さないことを確認した。保存後の extension 状態も次 skin へ漏らさない。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、script 側から `wb.changeSkin("MissingSkin")` を呼んだ時は `false` を返し、現在 skin・入力欄・候補表示をそのまま維持することを確認した。拡張入力中の changeSkin 失敗は no-op として扱える。
- 2026-04-18: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に候補 4 件へ拡張した dirty state のまま script 側から `wb.changeSkin("MissingSkin")` を呼んだ時、`false` を返しつつ現在 skin・入力欄空・候補 4 件をそのまま維持することを focused 4 件通過で確認した。候補拡張直後の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に候補 4 件へ拡張した dirty state のまま script 側から `wb.changeSkin("#umlFindTreeEve")` を呼んでも、`#input` / `#Selection` を tree / footer 側へ持ち越さないことを確認した。Get 後の成功側 carry-over 防止も実 host 正本で押さえた。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、`Get / Include / Save` まで進めた dirty state のまま `onClearAll` して `DefaultSmallWB` へ切り替えても、`#input` / `#Selection` を次 skin へ持ち越さないことを確認した。保存後の終端 reset でも extension 状態を漏らさない。
- 2026-04-17: `TagInputRelation` は MainWindow 実 host でも、`Get / Include / Save` まで進めた dirty state のまま `onSkinLeave` して `DefaultSmallWB` へ切り替えても、`#input` / `#Selection` を次 skin へ持ち越さないことを確認した。保存後の leave 終端でも extension 状態を漏らさない。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後の dirty state で `onClearAll -> 再 navigate -> onExtensionUpdated` しても、入力欄は空のまま初期候補 3 件を重複なく再生成できることを確認した。保存後の clear 終端も bridge 正本で押さえた。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後の dirty state で `onSkinLeave -> 再 navigate -> onExtensionUpdated` しても、入力欄は空のまま初期候補 3 件を重複なく再生成できることを確認した。保存後の leave 終端も bridge 正本で押さえた。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onRegistedFile -> Refresh()` の後にもう一度 `Refresh()` しても `fresh-series` を重複表示しないことを確認した。tree 更新の再入で同じ tag tree を積み増さない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onClearAll -> Refresh()` と `onSkinLeave -> onSkinEnter` の終端系を確認した。`#uml` の `Folders / Tags` と `#footer` の `ClearCache` は再入後も 1 回だけ維持され、tree / footer を二重生成しない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onRegistedFile -> Refresh()` で増えた `fresh-series` の tag tree を `onSkinLeave -> onSkinEnter` 後も 1 回だけ維持できることを確認した。callback 更新後の再入でも tree / footer を積み増さない。
- 2026-04-18: `umiFindTreeEve` は MainWindow 実 host でも、`onSkinLeave -> wb.changeSkin("#TagInputRelation")` の成功側で `#uml` / `#footer` を input / candidate 側へ持ち越さず、`#TagInputRelation` へ正しく再切替できることを確認した。
- 2026-04-18: `umiFindTreeEve` は MainWindow 実 host でも、`onSkinLeave` 直後に script 側から `wb.changeSkin("MissingSkin")` を呼んでも、`#uml` / `#footer` と `fresh-tag` を含む更新済み tree を現在 skin のまま維持することを確認した。leave 終端直後の失敗側も no-op として扱える。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、script 側から `wb.changeSkin("DefaultSmallWB")` を呼んだ時に、`#uml` / `#footer` を次 skin へ持ち越さず `DefaultSmallWB` へ切り替わることを確認した。tree/footer 拡張状態を次画面へ漏らさない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、dirty な tree / footer 状態のまま script 側から `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さず、再入直後の `#input` / `#Selection` は空のままになることを確認した。候補再生成の正本は既存の TagInputRelation 再入受け入れテスト群へ委ねる。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onRegistedFile -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さず、再入直後の `#input` / `#Selection` は空のままになることを確認した。登録後の tree 追加状態も次 skin へ漏らさない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onModifyPath -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さず、再入直後の `#input` / `#Selection` は空のままになることを確認した。path 更新後の tree/footer 状態も次 skin へ漏らさない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onRemoveFile -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さず、再入直後の `#input` / `#Selection` は空のままになることを確認した。remove 後の tree/footer 状態も次 skin へ漏らさない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onModifyPath -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("MissingSkin")` を呼んでも、`#uml` / `#footer` と更新後の folder tree を現在 skin のまま維持することを確認した。path 更新後の失敗側も no-op として扱える。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onRemoveFile -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("MissingSkin")` を呼んでも、`#uml` / `#footer` と削除後 tree 状態を現在 skin のまま維持することを確認した。remove 後の失敗側も no-op として扱える。
- 2026-04-18: `umiFindTreeEve` は MainWindow 実 host でも、`onRegistedFile -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("MissingSkin")` を呼んでも、`#uml` / `#footer` と `fresh-series` を含む更新済み tree を現在 skin のまま維持することを確認した。register 後の失敗側も no-op として扱える。
- 2026-04-19: `umiFindTreeEve` は MainWindow 実 host でも、`onModifyTags -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` と `fresh-tag` を input / candidate 側へ持ち越さないことを確認した。tag 更新後の success 境界も MainWindow 正本で押さえた。
- 2026-04-19: `umiFindTreeEve` は MainWindow 実 host でも、`onModifyTags -> Refresh()` 後の dirty state のまま script 側から `wb.changeSkin("MissingSkin")` を呼んでも、`#uml` / `#footer` と `fresh-tag` を含む更新済み tree を現在 skin のまま維持することを確認した。tag 更新後の failure 境界も no-op として扱える。
- 2026-04-17: `umiFindTreeEve` は runtime bridge 実 host でも、`onModifyPath -> Refresh()` 後の dirty state のまま `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さないことを focused 2 件通過で確認した。
- 2026-04-17: `umiFindTreeEve` は runtime bridge 実 host でも、`onRemoveFile -> Refresh()` 後の dirty state のまま `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さないことを focused 2 件通過で確認した。
- 2026-04-18: `umiFindTreeEve` は runtime bridge 実 host でも、`onModifyTags -> Refresh()` 後の dirty state のまま `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` と `fresh-tag` を input / candidate 側へ持ち越さないことを focused 1 件通過で確認した。tag 更新後の成功側 carry-over 防止も bridge 正本で押さえた。
- 2026-04-19: `umiFindTreeEve` は runtime bridge 実 host でも、`onModifyTags -> Refresh()` 後の dirty state のまま `wb.changeSkin("MissingSkin")` を呼んでも、`fresh-tag` を含む更新済み tree / footer を現在 skin のまま維持することを focused で確認した。tag 更新後の failure 境界も bridge 正本で押さえた。
- 2026-04-18: `umiFindTreeEve` は runtime bridge 実 host でも、`onClearAll` 直後に `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さないことを focused 1 件通過で確認した。終端 callback と skin API がぶつかる境界も bridge 正本で押さえた。
- 2026-04-19: `umiFindTreeEve` は runtime bridge 実 host でも、`onClearAll` 直後に `wb.changeSkin("MissingSkin")` を呼んでも、`fresh-tag` を含む更新済み tree / footer を現在 skin のまま維持することを focused で確認した。clear 終端直後の失敗側も no-op として扱える。
- 2026-04-18: `umiFindTreeEve` は runtime bridge 実 host でも、`onSkinLeave` 直後に `wb.changeSkin("#TagInputRelation")` を呼んでも、`#uml` / `#footer` を input / candidate 側へ持ち越さず `#TagInputRelation` へ切り替わることを focused 2 件通過で確認した。leave 終端直後の成功側切替も bridge 正本で押さえた。
- 2026-04-18: `umiFindTreeEve` は runtime bridge 実 host でも、`onSkinLeave` 直後に `wb.changeSkin("MissingSkin")` を呼んでも、`fresh-tag` を含む更新済み tree / footer を現在 skin のまま維持することを focused 2 件通過で確認した。leave 終端直後の失敗側も no-op として扱える。
- 2026-04-17: `umiFindTreeEve` は runtime bridge 実 host でも、`onModifyPath -> Refresh()` 後の dirty state のまま `wb.changeSkin("MissingSkin")` を呼んでも、更新後の folder tree と footer を現在 skin のまま維持することを focused 5 件通過で確認した。path 更新後の失敗側も no-op として扱える。
- 2026-04-17: `umiFindTreeEve` は runtime bridge 実 host でも、`onRemoveFile -> Refresh()` 後の dirty state のまま `wb.changeSkin("MissingSkin")` を呼んでも、削除後 tree 状態と footer を現在 skin のまま維持することを focused 5 件通過で確認した。remove 後の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` 後の dirty state のまま `wb.changeSkin("MissingSkin")` を呼んだ時に `false` を返し、現在 skin を維持したまま入力欄空・候補 4 件を保てることを focused 3 件通過で確認した。候補拡張直後の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` 後に `onSkinLeave` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時に `false` を返し、現在 skin を維持したまま入力欄空・候補表示空の拡張終端状態を保てることを確認した。候補拡張後 leave 終端の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` 後に `onClearAll` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時に `false` を返し、現在 skin を維持したまま入力欄空・候補表示空の拡張終端状態を保てることを確認した。候補拡張後 clear 終端の失敗側も no-op として扱える。
- 2026-04-19: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` 後に `onSkinLeave` / `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` が `false` を返した直後に `wb.changeSkin("#umlFindTreeEve")` しても、入力欄と候補表示を tree / footer 側へ持ち越さないことを focused 2 件通過で確認した。Get terminal の failure -> success 直列境界も bridge 正本で揃った。
- 2026-04-20: `TagInputRelation` の runtime bridge 実 host における `Include / Save -> onSkinLeave/onClearAll -> changeSkin("MissingSkin") -> changeSkin("#umlFindTreeEve")` は、`failure 単体` と `success 単体` は green だが、直列では最初の `MissingSkin` 結果待ち自体が安定しなかった。無理に main へ入れず、未固定の直列境界として分離した。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後の dirty state のまま `wb.changeSkin("MissingSkin")` を呼んだ時に `false` を返し、現在 skin を維持したまま入力欄空・候補 4 件の保存後状態を保てることを focused 3 件通過で確認した。保存後の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後に `onSkinLeave` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時に `false` を返し、現在 skin を維持したまま入力欄空・候補 4 件の保存後終端状態を保てることを確認した。保存後 leave 終端の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後に `onClearAll` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時に `false` を返し、現在 skin を維持したまま入力欄空・候補 4 件の保存後終端状態を保てることを確認した。保存後 clear 終端の失敗側も no-op として扱える。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後の dirty state のまま `wb.changeSkin("#umlFindTreeEve")` を呼んだ時に、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを focused 4 件通過で確認した。保存後の成功側 carry-over 防止も実 host 正本で押さえた。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後に `onSkinLeave` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時に、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを focused で確認した。保存後 leave 境界の成功側も bridge 正本で押さえた。
- 2026-04-18: `TagInputRelation` は runtime bridge 実 host でも、`Include / Save` 後に `onClearAll` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時に、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを focused で確認した。保存後 clear 終端の成功側も bridge 正本で押さえた。
- 2026-04-19: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` 後に `onSkinLeave` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時に、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを focused で確認した。Get 後 leave 終端の成功側も bridge 正本で押さえた。
- 2026-04-19: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` 後に `onClearAll` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時に、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを focused で確認した。Get 後 clear 終端の成功側も bridge 正本で押さえた。
- 2026-04-19: `TagInputRelation` は runtime bridge 実 host でも、bare な `onClearAll` または `onSkinLeave` の後に同一 skin へ再入しても、入力欄を空のまま初期候補 3 件を重複なく再生成できることを focused で確認した。MainWindow 側で先に確認していた terminal reset を bridge 正本へそろえた。
- 2026-04-19: `TagInputRelation` は runtime bridge 実 host でも、`ButtonGet()` で候補 4 件へ拡張した後に `onExtensionUpdated` を再実行すると、入力欄を空のまま初期候補 3 件へ重複なく戻せることを focused で確認した。Get 後の再候補生成境界も bridge 正本で揃えた。
- 2026-04-19: `umiFindTreeEve` は runtime bridge 実 host でも、bare な `onClearAll -> Refresh()` で `tree / footer` を二重生成せず、`onSkinLeave -> onSkinEnter` でも `Folders / Tags / ClearCache` を 1 回だけ再生成できることを focused で確認した。MainWindow 側の bare terminal 境界と bridge 側の正本が揃った。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onModifyTags -> Refresh()` で増えた `fresh-tag` を含む dirty state のまま `onSkinLeave -> onSkinEnter` しても、tag tree と footer を 1 回だけ再生成し、`fresh-tag` を重複表示しないことを確認した。更新後の再入でも tree/footer の積み増しを起こさない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onModifyPath -> Refresh()` 後の dirty state で `onSkinLeave -> onSkinEnter` しても、更新後の folder tree を 1 回だけ再生成し、`fresh` を重複表示しないことを確認した。path 更新後の再入でも folder tree を積み増さない。
- 2026-04-17: `umiFindTreeEve` は MainWindow 実 host でも、`onRemoveFile -> Refresh()` 後の dirty state で `onSkinLeave -> onSkinEnter` しても、削除済み `series-a` tree を復活させず、footer も 1 回だけ再生成することを確認した。remove 後の再入でも古い tree を戻さない。
- 2026-04-19: `umiFindTreeEve` は MainWindow 実 host でも、`onClearAll` 直後に script 側から `wb.changeSkin("MissingSkin")` を呼んでも、`#uml` / `#footer` と `fresh-tag` を含む更新済み tree を現在 skin のまま維持することを確認した。clear 終端直後の失敗側も no-op として扱える。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に `onSkinLeave` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時、現在 skin を維持したまま入力欄空・候補 4 件の Get 後状態を保てることを確認した。候補拡張後 leave 終端の失敗側は runtime bridge より保持寄りの実挙動である。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に `onClearAll` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時、現在 skin を維持したまま入力欄空・候補 4 件の Get 後状態を保てることを確認した。候補拡張後 clear 終端の失敗側は runtime bridge より保持寄りの実挙動である。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`Include / Save` 後に `onSkinLeave` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時、現在 skin を維持したまま入力欄空・候補 4 件の保存後終端状態を保てることを確認した。保存後 leave 終端の失敗側は runtime bridge と同じ保持挙動である。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`Include / Save` 後に `onClearAll` を挟んでから `wb.changeSkin("MissingSkin")` を呼んだ時、現在 skin を維持したまま入力欄空・候補 4 件の保存後終端状態を保てることを確認した。保存後 clear 終端の失敗側は runtime bridge と同じ保持挙動である。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に `onSkinLeave` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを確認した。候補拡張後 leave 終端の成功側も MainWindow 正本で押さえた。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に `onClearAll` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを確認した。候補拡張後 clear 終端の成功側も MainWindow 正本で押さえた。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`ButtonGet()` 後に `onClearAll` または `onSkinLeave` を挟んでから `onExtensionUpdated` を再実行すると、入力欄を空のまま `tag-24` を含まない初期候補へ重複なく戻せることを確認した。Get 後 terminal rerender の MainWindow 正本も揃った。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`Include / Save` 後に `onSkinLeave` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを確認した。保存後 leave 境界の成功側も MainWindow 正本で押さえた。
- 2026-04-19: `TagInputRelation` は MainWindow 実 host でも、`Include / Save` 後に `onClearAll` を挟んでから `wb.changeSkin("#umlFindTreeEve")` を呼んだ時、入力欄と候補表示を tree / footer 側へ持ち越さず `#umlFindTreeEve` へ切り替わることを確認した。保存後 clear 境界の成功側も MainWindow 正本で押さえた。
- 2026-04-19: `Search_table` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("DefaultSmallWB")` を呼んでも、`fresh-tag` を次 skin の `tag84` へ持ち越さず `idol` ベースの表示へ戻ることを確認した。tag 差分更新後の成功側 carry-over 防止も MainWindow 正本で押さえた。
- 2026-04-20: `Search_table` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを focused 3 件通過で確認した。MainWindow 側でも tag 差分更新後 clear 終端 success 境界を 1 本追加で固定できた。
- 2026-04-19: `Search_table` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と `tag84` の `fresh-tag` 表示、`thum_select / img_focus / title_focus` を維持することを確認した。tag 差分更新後の失敗側も no-op として扱える。
- 2026-04-19: `Chappy` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("DefaultSmallWB")` を呼んでも、`fresh-tag` を次 skin の `tag84` へ持ち越さず `idol` ベースの表示へ戻ることを確認した。tag 差分更新後の成功側 carry-over 防止も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを確認した。tag 差分更新後 clear 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `tag84` は空状態のまま復活しないことを確認した。tag 差分更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを確認した。tag 差分更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と `tag84 / tags_disp84` の `fresh-tag` 表示、`thum_focus thum_select / img_thum_focus` を維持することを確認した。tag 差分更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-19: `Chappy` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と `tag84 / tags_disp84` の `fresh-tag` 表示、`thum_focus thum_select / img_thum_focus` を維持することを確認した。tag 差分更新後の失敗側も no-op として扱える。
- 2026-04-19: `DefaultSmallWB` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("Search_table")` を呼んでも、`fresh-tag` を次 skin の `tag84` へ持ち越さず `idol` ベースの表示へ戻ることを確認した。minimal list skin の tag 差分更新後でも成功側 carry-over 防止を MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("Search_table")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを確認した。minimal list skin の tag 差分更新後 clear 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `tag84` は空状態のまま復活しないことを確認した。minimal list skin の tag 差分更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("Search_table")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを確認した。minimal list skin の tag 差分更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と `tag84` の `fresh-tag` 表示、`thum_focus thum_select / img_focus` を維持することを確認した。minimal list skin の tag 差分更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-19: `DefaultSmallWB` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と `tag84` の `fresh-tag` 表示、`thum_focus thum_select / img_focus` を維持することを確認した。minimal list skin の tag 差分更新後の失敗側も no-op として扱える。
- 2026-04-19: `Alpha2` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("DefaultSmallWB")` を呼んでも、`fresh-tag` を次 skin の `tag84` へ持ち越さず `idol` ベースの表示へ戻ることを確認した。Alpha2 の tag 差分更新後 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを確認した。Alpha2 の tag 差分更新後 clear 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `tag84` は空状態のまま復活しないことを確認した。Alpha2 の tag 差分更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `tag84` は `idol` ベースへ戻り `fresh-tag` を持ち越さないことを確認した。Alpha2 の tag 差分更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と `tag84` の `fresh-tag` 表示、`cthum thum_select / cimg_focus` を維持することを確認した。Alpha2 の tag 差分更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-19: `Alpha2` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と `tag84` の `fresh-tag` 表示、`cthum thum_select / cimg_focus` を維持することを確認した。Alpha2 の tag 差分更新後 failure 境界も no-op として扱える。
- 2026-04-19: `Alpha2` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("DefaultSmallWB")` を呼んでも、差分 thumb URL を次 skin の `img84.src` へ持ち越さず既定表示へ戻ることを確認した。Alpha2 の差分サムネ更新後 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `img84.src` は既定表示へ戻り更新済み thumb URL を持ち越さないことを確認した。差分サムネ更新後 clear 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Search_table` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `tag84` は空状態のまま復活しないことを確認した。tag 差分更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `img84.src` は既定表示へ戻り更新済み thumb URL を持ち越さないことを確認した。差分サムネ更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Search_table` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `img84.src` は既定表示へ戻り更新済み thumb URL を持ち越さないことを確認した。差分サムネ更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("Search_table")` しても、次 skin の `img84.src` は既定表示へ戻り更新済み thumb URL を持ち越さないことを確認した。minimal list skin の差分サムネ更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("DefaultSmallWB")` しても、次 skin の `img84.src` は既定表示へ戻り更新済み thumb URL を持ち越さないことを確認した。差分サムネ更新後 leave 終端 success 境界も MainWindow 正本で押さえた。
- 2026-04-19: `Alpha2` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `wb.changeSkin("MissingSkin")` を呼んでも、現在 skin と更新済み `img84.src`、`cthum thum_select / cimg_focus` を維持することを確認した。Alpha2 の差分サムネ更新後 failure 境界も no-op として扱える。
- 2026-04-21: `Chappy` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `img84.src` は空状態のまま復活しないことを確認した。差分サムネ更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-21: `Search_table` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `img84.src` は空状態のまま復活しないことを確認した。差分サムネ更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-21: `DefaultSmallWB` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `img84.src` は空状態のまま復活しないことを確認した。minimal list skin の差分サムネ更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-21: `Alpha2` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin を維持しつつ `img84.src` は空状態のまま復活しないことを確認した。差分サムネ更新後 clear 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Search_table` は MainWindow 実 host でも、`onModifyTags(84, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と `tag84` の `fresh-tag` 表示、`thum_select / img_focus / title_focus` を維持することを確認した。tag 差分更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Chappy` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と更新済み `img84.src`、`thum_select / img_thum_focus` を維持することを確認した。差分サムネ更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Search_table` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と更新済み `img84.src`、`thum_select / img_focus / title_focus` を維持することを確認した。差分サムネ更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `DefaultSmallWB` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と更新済み `img84.src`、`thum_focus thum_select / img_focus` を維持することを確認した。minimal list skin の差分サムネ更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-20: `Alpha2` は MainWindow 実 host でも、`onUpdateThum("db-main:84", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin と更新済み `img84.src`、`thum_select / cimg_focus` を維持することを確認した。差分サムネ更新後 leave 終端 failure 境界も MainWindow 正本で押さえた。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onModifyTags(77, ["idol","fresh-tag"])` 後に `wb.changeSkin(...)` 成功時は次 skin の `tag77` を `series-a` ベースへ戻し、`fresh-tag` を持ち越さないことを focused 8 件通過で確認した。build 出力 skin の tag 差分更新後 success 境界も bridge 正本で揃った。
- 2026-04-19: runtime bridge 実 host でも `Search_table` / `Chappy` / `DefaultSmallWB` / `Alpha2` の build 出力 skin 4 本に対して、`onModifyTags(77, ["idol","fresh-tag"])` 後に `wb.changeSkin("MissingSkin")` を呼んでも `false` を返し、現在 skin 名と `tag77` の `fresh-tag` 表示を維持することを focused 4 件通過で確認した。一覧系 skin の tag 差分更新後 failure 境界も bridge 正本で揃った。
- 2026-04-17: `wb.changeSkin` 成功時は、API service 内にだけ残っていた `addFilter / addWhere / addOrder` の overlay をまとめてクリアするようにした。Main search へ同期できなかった filter 条件だけが skin 切替後に新しい skin へ持ち越される筋を、focused test で塞いだ。
- 2026-04-17: `wb.changeSkin("DefaultSmallWB")` を MainWindow 実 host でも確認した。`Search_table` 上で `addFilter("idol") + addWhere("score >= 80") + addOrder("ファイル名(昇順)", 1)` を重ねた後でも、skin 切替後は `filter == ["idol"]` だけを維持し、`where / addOrder` は空へ戻したまま `DefaultSmallWB` へ再描画できる。
- 2026-04-17: `wb.changeSkin("MissingSkin")` の失敗側も MainWindow 実 host で確認した。`Search_table` 上で `addFilter / addWhere / addOrder` を積んだまま `false` を返しても host は再準備されず、現在 skin と overlay 状態を保ったまま描画を維持する。
- 2026-04-15: `WhiteBrowserSkinOrchestrator` 経由でも catalog cache が再利用されることを focused test で固定した。`GetAvailableSkinDefinitions() -> ApplySkinByName(...) -> GetAvailableSkinDefinitions()` は `miss 1 / hit 2`、html 更新後だけ `miss` が増える。catalog 再走査削減が MainWindow 相当の利用経路でも効いていることを確認した。
- 2026-04-15: `WhiteBrowserSkinOrchestrator` の `BuildAvailableSkinDefinitionSnapshot()` は `ResolveCurrentDefinition()` を掘り直さず、loaded definitions ベースで現在 skin を解決する形へ寄せた。これで `GetAvailableSkinDefinitions() -> ApplySkinByName(...) -> GetAvailableSkinDefinitions()` の余分な catalog hit を落とし、focused test でも `miss 1 / hit 2`、html 更新後は `miss 2 / hit 1` を維持できるようになった。
- 2026-04-16: `WhiteBrowserSkinCatalogService` は built-in 同名 external フォルダを snapshot 入口で除外するようにした。結果へ絶対採用しない skin の `HtmlPath` 解決や metadata 確認を踏まず、同種フォルダ更新では `catalog miss` を増やしにくくした。catalog focused は 11/11 通過した。
- 2026-04-16: `WhiteBrowserSkinCatalogService` の fallback HTML 解決は 1 回の列挙へまとめ、`.htm` 優先を保ったまま `EnumerateFiles("*.htm")` / `EnumerateFiles("*.html")` の二重走査を避けるようにした。custom fallback focused を追加し、catalog focused は 15/15 通過した。
- 2026-04-16: `WhiteBrowserSkinCatalogService.ResolveSkinHtmlPath(...)` は、標準名優先・前回 custom HTML 維持・fallback `.htm` 優先を 1 回の directory 列挙で同時に決める形へ寄せた。`main-view.html` 利用中に `other-view.htm` が後から追加されても、標準名が無い限り前回 custom HTML を維持する focused を追加し、catalog focused は 16/16 通過した。
- 2026-04-16: `BuildCatalogSnapshot(...)` は html path 解決と metadata 取得を同じ helper で返す形へ寄せ、`Resolve -> File.Exists -> FileInfo` の往復を減らした。directory 時刻不変でも html 更新を拾う既存意味論は維持し、catalog focused は 16/16 維持した。
- 2026-04-15: `MainWindow.WebViewSkin` の refresh batch も `skin-webview` ログへ begin / flush を残すようにした。flush 時は `preferred` / `batched` / `skinRaw` / `db` まで残すので、`refresh deferred -> refresh batch flush -> refresh queued` と `skin-catalog` の `cache hit / miss` を、同じ `debug-runtime.log` 上で時系列に追える。
- 2026-04-16: `skin-webview` の `refresh deferred / queued / batch begin / batch flush` には `batch=btXXXX` と `request=rqXXXX` の短い識別子も載せ、同じ切替単位の流れを 1 本で追いやすくした。
- 2026-04-16: `request=rqXXXX` は `host prepare begin` / `host navigate failed` / `refresh skipped stale` / `host presentation` にも引き継ぎ、queue された refresh が apply 完了までどう流れたかを追いやすくした。
- 2026-04-14: compat runtime の `getFindInfo` cache は epoch を持つようにし、古い `getFindInfo()` 応答が新しい `onUpdate.findInfo` を後から上書きしないようにした。旧同期前提 skin の `findInfo` 揺れを抑える防御であり、compat 統合テストで stale 応答破棄を確認した。
- 2026-04-14: `Chappy` / `Alpha2` / `DefaultSmallWB` は MainWindow 実 host でも `wb.addWhere("score >= 18")` による skin 側 where overlay と DOM 再描画を確認した。`SearchKeyword` は空のまま、`wb.getFindInfo().where` と DOM が揃う形である。
- 2026-04-14: `Search_table` は MainWindow 実 host で `wb.addOrder("ファイル名(降順)", 1)` の受け入れも確認した。`MainVM.DbInfo.Sort` へは反映せず、`wb.getFindInfo().sort` に `#ファイル名(降順)` を持つ skin 側 overlay として DOM 順が `title84 -> title91 -> title77` へ更新される。
- 2026-04-14: `Alpha2` も MainWindow 実 host で `wb.addOrder("ファイル名(降順)", 1)` の受け入れを確認した。`ConfigLoaded = false` と `wb.onSkinEnter()/wb.update()` で shared UI 状態を整えた後、`wb.getFindInfo().sort` に `#ファイル名(降順)` を持つ skin 側 overlay として card DOM 順が `thum84 -> thum91 -> thum77` へ更新される。
- 2026-04-14: `Chappy` も MainWindow 実 host で `wb.addOrder("ファイル名(降順)", 1)` の受け入れを確認した。`wb.getFindInfo().sort` に `#ファイル名(降順)` を持つ skin 側 overlay として、card DOM 順が `thum84 -> thum91 -> thum77` へ更新される。
- 2026-04-14: `DefaultSmallWB` も MainWindow 実 host で `wb.addOrder("ファイル名(降順)", 1)` の受け入れを確認した。`getFindInfo().sort` への露出は薄いが、最小一覧 skin でも DOM 順が `title84 -> title91 -> title77` へ更新され、skin 側 sort overlay は有効である。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onModifyTags(77, ["idol","fresh-tag"])` 後に `onSkinLeave` または `onClearAll` を流して同一 skin へ再入すると、`fresh-tag` を持ち越さず初期 tag 表示へ戻ることを focused 8 件通過で確認した。build 出力 skin の terminal re-enter 境界も bridge 正本で揃った。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onUpdateThum("db-main:77", "...")` 後に `onSkinLeave` または `onClearAll` を流して同一 skin へ再入すると、更新済み thumb URL を持ち越さず初期 thumb 表示へ戻ることを focused 8 件通過で確認した。build 出力 skin の差分サムネ更新後 terminal re-enter 境界も bridge 正本で揃った。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onUpdateThum("db-main:77", "...")` 後に `onSkinLeave` または `onClearAll` を挟んで `wb.changeSkin(...)` しても、次 skin の `img77.src` へ更新済み thumb URL を持ち越さず既定表示へ戻ることを focused 8 件通過で確認した。build 出力 skin の差分サムネ更新後 terminal success 境界も bridge 正本で揃った。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onUpdateThum("db-main:77", "...")` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin 名と更新済み thumb URL を維持することを focused 4 件通過で確認した。build 出力 skin の差分サムネ更新後 leave 終端 failure 境界も bridge 正本で押さえた。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onUpdateThum("db-main:77", "...")` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin 名を維持したまま `img77.src` は空のまま復活しないことを focused 4 件通過で確認した。`onClearAll` failure 境界は `onSkinLeave` と非対称で、空状態維持が bridge 正本である。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onModifyTags(77, ["idol","fresh-tag"])` 後に `onClearAll` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin 名を維持したまま `tag77` は空のまま復活しないことを focused 4 件通過で確認した。build 出力 skin の tag 差分更新後 clear 終端 failure 境界も `onSkinLeave` と非対称で、空状態維持が bridge 正本である。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onModifyTags(77, ["idol","fresh-tag"])` 後に `onSkinLeave` を挟んで `wb.changeSkin("MissingSkin")` しても、現在 skin 名を維持したまま `tag77` の `idol / fresh-tag` 表示を保つことを focused 4 件通過で確認した。build 出力 skin の tag 差分更新後 leave 終端 failure 境界も bridge 正本で押さえた。
- 2026-04-19: runtime bridge 実 host でも `Search_table / Chappy / DefaultSmallWB / Alpha2` の build 出力 skin 4 本に対して、`onModifyTags(77, ["idol","fresh-tag"])` 後に `onSkinLeave` または `onClearAll` を挟んで `wb.changeSkin(...)` しても、次 skin の `tag77` は `series-a` ベースへ戻り `fresh-tag` を持ち越さないことを focused 8 件通過で確認した。build 出力 skin の tag 差分更新後 terminal success 境界も bridge 正本で揃った。
- 2026-04-19: `umiFindTreeEve` は runtime bridge 実 host でも `onModifyTags -> Refresh()` 後に `onSkinLeave` または `onClearAll` を挟んで `wb.changeSkin("#TagInputRelation")` しても、`#uml / #footer` と `fresh-tag` を input / candidate 側へ持ち越さないことを focused 2 件通過で確認した。tag 更新後 terminal success 境界も bridge 正本で揃った。
- 2026-04-19: `umiFindTreeEve` は runtime bridge 実 host でも `onModifyPath -> Refresh()` または `onRemoveFile -> Refresh()` 後に `onSkinLeave` または `onClearAll` を挟んで `wb.changeSkin("#TagInputRelation")` しても、更新済み `#uml / #footer` を input / candidate 側へ持ち越さないことを focused 4 件通過で確認した。path/remove 更新後 terminal success 境界も bridge 正本で揃った。
- 2026-04-20: `umiFindTreeEve` は runtime bridge 実 host でも `onRegistedFile / onModifyTags / onModifyPath / onRemoveFile -> Refresh()` 後に `onSkinLeave -> Refresh()` しても、更新済み tree / footer を 1 回だけ再生成し、register は `fresh-series` を重複表示せず、tag は `fresh-tag` を重複表示せず、path は更新後 folder tree を重複表示せず、remove は削除済み tree を復活させないことを focused 8 件通過で確認した。leave 再描画境界も bridge 正本で clear 系と対称になった。
- 2026-04-20: `TagInputRelation` の MainWindow 実 host における `Get後 -> onSkinLeave/onClearAll -> changeSkin("MissingSkin") -> changeSkin("#umlFindTreeEve")` は、runtime bridge 側のようにはまだ正本化していない。WPF 側の `MS.Win32.HwndSubclass.SubclassWndProc` 起点 fail-fast が混ざるため、無理に main へ入れず残差扱いで分離した。
- 2026-04-21: `TagInputRelation` の MainWindow 実 host における bare な `onSkinLeave/onClearAll -> changeSkin("#umlFindTreeEve")` も、`terminal 単体` と `Get/Save 後 terminal -> success` は green だが、この直列だけは `MS.Win32.HwndSubclass.SubclassWndProc` 起点 fail-fast が混ざった。無理に main へ入れず、未固定の success 境界として分離した。
- 2026-04-21: `TagInputRelation` は MainWindow 実 host でも、bare な `onClearAll` または `onSkinLeave` の直後に `wb.changeSkin("MissingSkin")` しても、`false` を返しつつ現在 skin と入力欄空・候補 4 件の終端状態を維持することを focused 4 件通過で確認した。bare terminal の failure 境界は実 host 正本で押さえた。
- 2026-04-21: `TagInputRelation` は runtime bridge 実 host でも、bare な `onClearAll` または `onSkinLeave` の直後に `wb.changeSkin("MissingSkin")` しても、`false` を返しつつ現在 skin と入力欄空・候補 4 件の終端状態を維持することを focused 3 件通過で確認した。bare terminal の failure 境界は bridge 正本でも押さえた。
- 2026-04-21: `TagInputRelation` は runtime bridge 実 host でも、bare な `onSkinLeave` または `onClearAll` の直後に `wb.changeSkin("#umlFindTreeEve")` しても、初期候補 3 件ベースの終端状態から `#input / #Selection` を tree / footer 側へ持ち越さないことを focused 2 件通過で確認した。bare terminal の success 境界も bridge 正本で押さえた。
- 2026-04-22: `umiFindTreeEve` の MainWindow 実 host における `onModifyTags -> Refresh() -> onSkinLeave/onClearAll -> wb.changeSkin("#TagInputRelation")` は、新規 2 件自体は通るものの focused 束の teardown で `MS.Win32.HwndSubclass.SubclassWndProc` 起点の fail-fast が混ざった。runtime bridge 側は green だが、MainWindow 側はまだ未固定の success 境界として分離する。
- 2026-04-20: build 出力 skin 4 本の runtime bridge における `tag差分更新後 -> terminal -> changeSkin("MissingSkin") -> changeSkin(nextSkin)` は、`failure 単体` と `success 単体` は green だが、直列では 2 回目の `changeSkin` 完了待ちが timeout した。ここは未固定の直列境界として切り分け済み。
- 2026-04-19: `umiFindTreeEve` は MainWindow 実 host でも `onRegistedFile -> Refresh()` 後に `onClearAll -> Refresh()` しても `fresh-series` の tag tree と `ClearCache` footer を 1 回だけ再生成し、重複表示しないことを focused 6 件通過で確認した。
- 2026-04-19: `umiFindTreeEve` は MainWindow 実 host でも `onModifyTags -> Refresh()` 後に `onClearAll -> Refresh()` しても `fresh-tag` の tag tree と `ClearCache` footer を 1 回だけ再生成し、重複表示しないことを focused 3 件通過で確認した。
- 2026-04-19: `umiFindTreeEve` は MainWindow 実 host でも `onModifyPath -> Refresh()` 後に `onClearAll -> Refresh()` しても更新後の folder tree を重複表示せず、`onRemoveFile -> Refresh()` 後に `onClearAll -> Refresh()` しても削除済み tree を復活させないことを focused 6 件通過で確認した。clear 再描画境界も MainWindow 正本で揃った。
- 2026-04-20: `umiFindTreeEve` は MainWindow 実 host でも `onRegistedFile / onModifyTags / onModifyPath / onRemoveFile -> Refresh()` 後に `onSkinLeave -> Refresh()` しても、更新済み tree / footer を 1 回だけ再生成し、register は `fresh-series` を重複表示せず、tag は `fresh-tag` を重複表示せず、path は更新後 folder tree を重複表示せず、remove は削除済み tree を復活させないことを focused 6 件通過で確認した。leave 再描画境界も MainWindow 正本で clear 系と対称になった。

## 参考ドキュメント

- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `WhiteBrowserSkin/Docs/ExternalSkinApiUsageSummary_2026-04-07.md`
- `WhiteBrowserSkin/Docs/調査結果_WebView2_WBskin再調査_実装進捗反映_2026-04-02.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
- `WhiteBrowserSkin/Docs/障害対応_WebView2サムネ契約_GDI枯渇抑制_2026-04-09.md`
