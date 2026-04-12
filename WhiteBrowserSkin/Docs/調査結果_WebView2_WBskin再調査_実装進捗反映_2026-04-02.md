# 調査結果 WebView2 / WB skin 再調査 実装進捗反映 2026-04-02

最終更新日: 2026-04-12

変更概要:
- 2026-04-02 時点の再調査メモを、callback 互換強化 / `wb.sort` / legacy alias / テスト追加の実装系列に追随させた
- 第1段階仕上げとして `selectThum` 分離、複数選択反映、`onSkinLeave` / `onClearAll` 固定、`scroll-id` / `wb.scrollTo` 強化を反映した
- tag 系 API (`wb.addTag` / `wb.removeTag` / `wb.flipTag`) と `onModifyTags` 接続、DB readback 付き永続化確認を反映した
- `onUpdateThum` の第1段階として、通常生成 / rescue 反映成功から現在表示中 skin へ差分 callback を返す導線を反映した
- host 側で `handleSkinLeave` を明示 dispatch し、`NavigateToString` / blank 遷移を `NavigationCompleted` まで待つ第2段階を反映した
- `wb.addWhere` / `wb.addOrder` の第1段階として、検索結果へ重ねる overlay 条件と `override` / 空文字クリアを反映した
- `wb.getFindInfo` / `wb.getFocusThum` / `wb.getSelectThums`、`wb.addFilter` / `wb.removeFilter` / `wb.clearFilter` の第1段と mixed-query 整理を反映した
- DB切替 / external->external / external->built-in / minimal reload の自動回帰追加を反映した
- runtime 未導入 / html missing / host 初期化失敗を標準ヘッダー通知と `skin-webview` ログで見分ける第1段を反映した
- fallback 通知からの `再試行` 導線を反映した
- fallback 通知からの `Runtimeを入手` 導線を反映した
- fallback 通知からの `ログを開く` 導線を反映した
- `WhiteBrowserSkinRenderCoordinator` の同一 skin HTML 再利用キャッシュを反映した
- `wb.getInfos(startIndex, count)` と range 系 API の既定件数化を反映した
- `SimpleGridWB` の段階読込 baseline と、compat 既定 `onUpdate` fallback の append 入口を反映した
- `TutorialCallbackGrid` と `WhiteBrowserDefaultList` で、MainWindow 実 host 上の `startIndex > 0` append と append 後の先頭 `find` 復帰まで自動回帰へ載せた
- `seamless-scroll` は初回描画直後に勝手に追記せず、実 scroll 起点で次ページを要求する形へ整理し、`TutorialCallbackGrid` と config 駆動の `WhiteBrowserDefaultList` で回帰固定した
- 4/2 時点で「次の山」としていた項目のうち、今回実装ラインへ上がったものと、まだ残るものを分け直した

## 1. 目的

WebView2 による WhiteBrowser 互換 skin 対応は、4/2 時点で「起動する」「最小 API がある」段階へ進んでいた。

今回の作業系列で、

- callback 互換をもう一段厚くした
- `wb.sort` を追加した
- `wb.getProfile` / `wb.writeProfile` / `wb.changeSkin` を MainWindow へ接続した
- legacy alias を載せた
- 回帰テストを増やした
- `wb.getFindInfo` / `wb.getFocusThum` / `wb.getSelectThums` と filter 系第1段を進めた
- `wb.selectThum` を `focusThum` から分離した
- `onSkinLeave` / `onClearAll` と `scroll-id` / `wb.scrollTo` を実運用寄りに寄せた
- `wb.addTag` / `wb.removeTag` / `wb.flipTag` を既存のタグ更新導線へ接続した
- `onUpdateThum` を通常生成 / rescue 反映の成功経路へ接続した
- runtime 不在 / html 欠落 / host 初期化失敗の理由を、WPF fallback 後も標準ヘッダーから辿れるようにした
- `WhiteBrowserSkinRenderCoordinator` で同一 skin HTML の `ReadAllBytes + Normalize` を再利用する第1段キャッシュを入れた
- `wb.getInfos(startIndex, count)` と、range 系 API の `count` 省略時既定件数を入れた
- `SimpleGridWB` を初回ページ + 追加ページの baseline とし、`WhiteBrowserDefaultList` でも `onCreateThum` だけの既定 fallback append を確認した
- `SimpleGridWB` は「続きを読み込む」だけでなく scroll でも追加ページを読める baseline へ更新した
- `TutorialCallbackGrid` でも MainWindow 実 host 上の `update(200, 1)` 追記と、その直後の `find("Movie201", 0)` 先頭復帰を確認した
- `TutorialCallbackGrid` では実 scroll 後だけ `seamless-scroll` 追記し、先頭 focus を維持できることを MainWindow 実 host で確認した
- `WhiteBrowserDefaultList` では `scrollSetting()` を呼ばなくても config の `seamless-scroll : 2` で scroll 起点追記できることを実 fixture で確認した
- `WhiteBrowserDefaultGrid` / `Small` / `Big` でも config の `seamless-scroll : 2` だけで scroll 起点追記できることを実 fixture で確認した
- MainWindow 実 host では `WhiteBrowserDefaultList` の config 駆動 `seamless-scroll` 追記を代表ケースとして確認した
- `WhiteBrowserDefaultGrid` / `Small` / `Big` は実 WebView2 runtime bridge で config 駆動 `seamless-scroll` を固定し、検索後 `seamless-scroll` 追記と `find("Movie201", 0)` 先頭復帰も runtime bridge 側で確認した
- MainWindow 実 host でも `WhiteBrowserDefaultList` の config 駆動 `seamless-scroll` 追記を確認した
- MainWindow 実 host でも `WhiteBrowserDefaultList` の `wb.find("Movie", 0)` 後に `seamless-scroll` で追加ページを継続できることを確認した
- MainWindow 実 host でも `WhiteBrowserDefaultList` の `seamless-scroll` 追記後 `find("Movie201", 0)` の先頭復帰を確認した
- `SimpleGridWB` は MainWindow 実 host で、検索後でも scroll による追加ページ継続と `260 items` 到達を確認した
- `SimpleGridWB` は MainWindow 実 host で、空振り追記後に追加要求を自律停止できることも確認した
- `WhiteBrowserDefaultGrid` / `Small` / `Big` は実 WebView2 runtime bridge 統合テストで、`wb.find("Movie", 0)` 後も `seamless-scroll` が `update(startIndex=2)` を要求して追加ページを継続できることを確認した
- `WhiteBrowserSkinHostControl` は終了時に WebView2 実体まで明示 dispose するようにした
- `MainWindowWebViewSkinIntegrationTests` は、MainWindow の config 単独 `seamless-scroll` を `WhiteBrowserDefaultList` 代表へ整理し、MainWindow の scroll 代表ケースを `WhiteBrowserDefaultList` / `TutorialCallbackGrid` / `SimpleGridWB` へ寄せた
- compat runtime の `seamless-scroll` も、`update(startIndex=2)` 空振り後に再要求を残さず止めるようにした
- `MainWindowWebViewSkinIntegrationTests` は 39/39、`WhiteBrowserSkinRuntimeBridgeIntegrationTests | WhiteBrowserSkinCompatScriptIntegrationTests | WhiteBrowserSkinEncodingNormalizerTests | WhiteBrowserSkinRenderCoordinatorTests` は 29/29 を確認した
- 上記構成へ整理したあと、combined broad も 68/68 通過を確認した

ため、4/2 時点の「次の本命」を現在地に合わせて更新する。

## 2. 4/2 時点の結論

4/2 時点で成立していたものは次である。

1. 外部 skin は実アプリ起動で成立していた
2. `wb.update / getInfo / getInfos / find / focusThum` までの最小 bridge は入っていた
3. サムネ契約は `thumbUrl / thumbRevision / sourceKind / 寸法 DTO` まで本体実装へ入っていた
4. その結果、次の本命は「host 起動可否」ではなく、「WB callback 互換の厚み」と「操作 API 拡張」へ移っていた

## 3. 2026-04-10 時点で前進した点

### 3.1 callback 互換の第一段が入った

- `onCreateThum` を軸に、旧 WB スキンが一覧描画で使う callback の接続を強化した
- `onSetFocus` / `onSetSelect` を旧 WB スキンが扱いやすい引数形へ寄せた
- `onSkinEnter` とスクロール初期化導線を追加し、skin 起動直後の初期化フローを bridge 側で受けられるようにした

4/2 時点では「これから実装する本命」だったが、今回の作業系列で実装ラインへ上がった。

### 3.2 `wb.sort` が最小セットから実運用セットへ入った

- skin 側からの並び替え要求を、本体の既存検索 / 並び替え導線へ流せるようにした
- これにより `find` に続いて、一覧 UI で必要になる基本操作の一つが skin 側から触れるようになった

### 3.3 `profile` / `changeSkin` 系も最小接続が入った

- `wb.getProfile` / `wb.writeProfile` は現在 DB + 現在 skin の `profile` テーブルへ接続した
- `wb.changeSkin` は既存の skin 適用導線へ流せるようにした
- `wb.selectThum` は `focusThum` から分離し、WPF 側の選択状態と複数選択反映へ接続した
- `focusedMovieId` を返し、選択解除後に WPF 側で移った実フォーカスへ compat runtime が追従できるようにした

### 3.4 lifecycle / scroll の第1段階も入った

- compat runtime が `multi-select` / `scroll-id` を読めるようになった
- `onSkinLeave` / `onClearAll` の意味論と発火順を compat runtime 側で固定した
- `wb.scrollTo` はスクロール対象要素の解決を強化し、inner pane 前提 skin を扱いやすくした

### 3.5 tag 系 API も実装ラインへ上がった

- `wb.addTag` / `wb.removeTag` / `wb.flipTag` を bridge へ追加した
- tag 変更は既存のタグ更新導線へ流し、DB 更新後は readback で永続化結果を確認する形にした
- compat runtime は tag 変更後に `onModifyTags` を返し、WB 側スクリプトが callback ベースで追従できるようにした

### 3.6 `onUpdateThum` の第1段階も入った

- `recordKey / thumbUrl / thumbRevision / thumbSourceKind / sizeInfo` を返す callback payload を固定した
- 通常サムネ生成成功と rescue 反映成功から、現在表示タブと一致する場合だけ `onUpdateThum` を dispatch するようにした
- compat runtime は位置引数ベースの callback 形へも流せるため、旧 WB スキンの callback 慣習に合わせやすい

### 3.7 host refresh / lifecycle の第2段階も入った

- `WhiteBrowserSkinHostControl` が、外部 skin の再 navigate 前と blank fallback 前に `handleSkinLeave` を明示 dispatch するようになった
- `NavigateToString` と blank 遷移は `NavigationCompleted` まで待つため、scheduler が document 遷移まで直列化しやすくなった
- 実 WebView2 統合テストで、host 側 `HandleSkinLeaveAsync` が `focus false -> select false -> clear -> leave` を 1 回だけ返すことを固定した

### 3.8 legacy alias を追加した

- DTO / callback payload に、旧 WB スキンが参照する名前を追加した
- 代表例:
  - `id`
  - `title`
  - `thum`
  - `exist`
  - `select`
- 一方で `recordKey` / `thumbRevision` / `thumbUrl` / 寸法情報といった新契約は維持し、互換と拡張を両立する構成にした

### 3.9 `addWhere` / `addOrder` の第1段階も入った

- `wb.addWhere` は SQL 風の追加条件を現在結果へ重ね、即時 `update` payload を返す形にした
- `wb.addOrder` は sort 名 / sort ID / `{...}` の SQL 風 `ORDER` 断片を受け、`override=0/1` と空文字クリアを扱えるようにした
- 実装は WPF 側の本体検索器を書き換えず、外部 skin runtime の overlay 条件として持つ形に寄せた

### 3.10 テスト対象が「最小成立」から「互換回帰」へ広がった

- callback 互換
- `wb.sort`
- `wb.addWhere` / `wb.addOrder`
- `wb.getProfile` / `wb.writeProfile` / `wb.changeSkin`
- tag 系 API
- `onUpdateThum`
- legacy alias
- 複数選択反映
- `focusedMovieId`
- lifecycle の発火順
- `scroll-id` 経路
- compat script の callback 回数確認

を API service テストと compat script 統合テストへ追加 / 補強することで、`SimpleGridWB` 専用の最小成功から一歩進めた。

### 3.11 大量件数対策の入口も入った

- `WhiteBrowserSkinRenderCoordinator` は、同じ skin HTML を再表示するたびに `ReadAllBytes + Normalize` をやり直さず、正規化済み document を再利用する第1段キャッシュを持つようになった
- `wb.getInfos` は `movieIds` / `recordKeys` に加えて `startIndex + count` の範囲取得を受けられるようになった
- compat runtime の range 系 API は、`count` 省略時に `g_thumbs_limit` / `defaultThumbLimit` を既定値として送るようにした
- `SimpleGridWB` は `wb.update(0)` / `wb.find(keyword, 0)` と `wb.getInfos(startIndex)` を組み合わせる段階読込 baseline へ進めた
- compat runtime の既定 `onUpdate` fallback も `startIndex > 0` の `update` を append として扱えるため、`onCreateThum` だけを持つ既存 WB skin でもページ追記の入口までは入った

ここは「本命の大量件数対策が完了した」ではなく、あくまで **入口実装が入った段階** である。

## 4. 現在地 (2026-04-10)

### 4.1 もうできていること

- 外部 skin の catalog / config 読み込み
- `system.skin` / `profile` を使った skin 永続化
- 実アプリでの WebView2 host 表示
- runtime 未導入時の fallback 分岐
- minimal host chrome
- `wb.update / find / sort / getInfo / getInfos / getProfile / writeProfile / changeSkin / focusThum / selectThum / addTag / removeTag / flipTag / scrollTo`
- `thum.local` による managed / external / placeholder サムネ配信
- `thumbRevision` 付き URL
- legacy alias 付き DTO / callback payload
- callback 互換の第一段
  - `onCreateThum`
  - `onSetFocus`
  - `onSetSelect`
  - `onSkinEnter`
  - `onSkinLeave`
  - `onClearAll`
- `multi-select` / `scroll-id` を読む compat runtime
- 実機ログと回帰テストによる切り分け

### 4.2 まだ未完のもの

#### A. 実ホスト受け入れ確認

- DB 切替 / skin 切替直後の実機受け入れ確認
- WebView2 再初期化ありの経路確認
- 自動テストでは DB切替後の host 維持と external -> external 切替までは補強済み

#### B. 操作 API の残件

未着手または本格未着手の中心は次である。

- native タグバー以外の自由入力検索と checked 状態の境界整理

#### C. 大量件数向けの本命対策

4/1 の計画にある本命はまだ未完だが、入口として次は反映済みである。

- 同一 skin HTML の正規化済み再利用キャッシュ
- `wb.getInfos(startIndex, count)` の範囲取得
- compat range API の既定件数
- `SimpleGridWB` の段階読込 baseline
- `onCreateThum` だけの既定 fallback append 入口

- 差分更新
- 仮想スクロール
- 大量 DOM 抑制
- 可視範囲優先ロード

## 5. 見立て

### 5.1 「WebView2 が出ない」は、もう主戦場ではない

host 仮マウント、refresh 制御、実機ログ、fallback が揃っており、
主戦場は表示可否そのものではなく互換の厚みへ移っている。

### 5.2 いまのボトルネックは「旧 WB skin をどこまで無修正で受けるか」

今回の作業で callback 互換の第一段と `wb.sort` が入り、
外部 skin は「動作確認用 sample を動かす」段階から、
「既存 WB skin fixture をどこまでそのまま通せるか」を詰める段階へ進んだ。

### 5.3 次の仕事は受け入れ確認 / filter 互換の厚み出し / 大量件数対策

本当に残っている根幹は次である。

- 実ホストの DB 切替 / skin 切替受け入れ確認
- native タグバー以外の自由入力検索と checked 状態の境界整理
- 大量件数時の差分更新と可視範囲最適化

## 6. 次の本命候補

優先順のおすすめは次である。

1. 実ホストの DB 切替 / skin 切替受け入れ確認
2. filter 互換の厚み出し
3. 大量件数対策

## 7. 再調査結論

2026-04-11 時点の WebView2 / WB skin は、

- host 表示
- bridge 最小セット
- callback 互換の第一段
- `wb.sort`
- `profile` / `changeSkin` 系の最小接続
- `getFindInfo` / `getFocusThum` / `getSelectThums`
- `addFilter` / `removeFilter` / `clearFilter`
- tag 系 API
- `onUpdateThum` 第1段階
- `addWhere` / `addOrder` 第1段階
- `selectThum` 分離と複数選択反映
- `onSkinLeave` / `onClearAll` 固定
- `scroll-id` / `wb.scrollTo` 強化
- legacy alias
- サムネ契約
- テスト補強

まで進んでいる。

したがって、今後の論点は

- 起動するか
- 最小 bridge があるか

ではなく、

- host refresh / lifecycle を実機受け入れまでどこまで無修正互換へ寄せるか
- filter 互換を native タグバー意味論へどこまで寄せるか
  - 現時点の第1段は exact tag 構文での `SearchKeyword` 同期ベース
- 大量件数でもテンポを維持できるか

である。

## 8. 参照

- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `WhiteBrowserSkin/Docs/WhiteBrowser_SkinResearch_2026-04-01.md`
- `WhiteBrowserSkin/Docs/ExternalSkinApiUsageSummary_2026-04-07.md`
- `WhiteBrowserSkin/Docs/Progress_SkinFeature_2026-04-07.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
- `WhiteBrowserSkin/Docs/障害対応_WebView2サムネ契約_GDI枯渇抑制_2026-04-09.md`
