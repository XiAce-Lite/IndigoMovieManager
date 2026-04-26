# 攻略チェックリスト TutorialCallbackGrid 2026-04-11

## 目的

- `TutorialCallbackGrid` を、外部スキン互換の正本 fixture として段階攻略する
- 実装前に「何が既に通っていて、何を次に固定するか」を明確にし、作業をぶらさない

## 対象

- スキン本体
  - `Tests/IndigoMovieManager.Tests/TestData/WhiteBrowserSkins/TutorialCallbackGrid/TutorialCallbackGrid.htm`
- compat runtime
  - `skin/Compat/wblib-compat.js`
- host bridge
  - `WhiteBrowserSkin/Runtime/WhiteBrowserSkinRuntimeBridge.cs`
  - `Views/Main/MainWindow.WebViewSkin.Api.cs`

## この fixture が使う連携面

### 使用コマンド

- `wb.scrollSetting(g_seamless_scroll, "")`
- `wb.update(0, g_thumbs_limit)`
- `wb.focusThum(mvs[0].id)`

### 使用 callback

- `wb.onSkinEnter`
- `wb.onUpdate`
- `wb.onCreateThum`
- `wb.onSetFocus`
- `wb.onSetSelect`

### config 依存

- `multi-select : 1`
- `seamless-scroll : 2`
- `scroll-id` なし
  - 現在の compat runtime では `view` へ fallback する

## 現在の到達点

### 既に通っている土台

- `onSkinEnter` の呼び口は compat runtime 側で成立している
- `scrollSetting(mode > 0)` は `wb.update(...)` へ流れる
- `wb.update()` は `onUpdate` を返す
- `update/find/sort/addWhere/addOrder/addFilter/removeFilter/clearFilter` の **先頭再更新 (`startIndex <= 0`)** は compat runtime 側で `onClearAll` を先に通してから `onUpdate` を返す
- `wb.focusThum()` は `onSetFocus` / `onSetSelect` の二重発火を抑えつつ返せる
- `onSkinLeave` / `onClearAll` は compat runtime 側で意味論を持っている
- `prototype.js` compat 側で `Insertion.Top` / `Insertion.Bottom` の最小実装を持ち、旧 `onCreateThum` の挿入呼びを吸収できる
- host 側の leave は `focus false -> select false -> clear -> leave` の順で 1 回だけ返す形を実 WebView2 テストで固定済み

### 既存自動テストで担保済みの線

- `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinCompatScriptIntegrationTests.cs`
  - focus / select の二重発火防止
  - lifecycle 順
  - `scrollSetting` / `scrollTo`
  - `onUpdateThum`
- `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinRuntimeBridgeIntegrationTests.cs`
  - `HandleSkinLeaveAsync()` の順序固定
  - `TutorialCallbackGrid` 実 fixture の `onSkinEnter -> update -> onCreateThum -> focusThum -> leave clear`
  - `TutorialCallbackGrid` 同一 fixture 再 navigate の `focus false -> select false -> clear -> leave`
- `Tests/IndigoMovieManager.Tests/MainWindowWebViewSkinIntegrationTests.cs`
  - `TutorialCallbackGrid` 実 fixture の DB切替後再描画
  - `TutorialCallbackGrid -> WhiteBrowserDefaultList` 実 fixture 切替
  - `TutorialCallbackGrid` 実 fixture の `find / sort / addFilter` 再更新
  - `TutorialCallbackGrid` 実 fixture の minimal reload
  - `TutorialCallbackGrid` 実 fixture から `built-in` 復帰

### まだ fixture 固有に詰めたい線

- `onUpdate` の末尾で `wb.focusThum(...)` を呼ぶため、描画中 callback と selection 更新の再入が絡みやすい
- `startIndex > 0` の追記更新を今後入れる時に、先頭再更新と append の意味論を崩さないこと

## 攻略チェックリスト

### 1. 起動初期化

- `DOMContentLoaded` 後に `onSkinEnter` が 1 回だけ走る
- `wb.scrollSetting(2, "")` が成功し、`wb.update(0, g_thumbs_limit)` へ流れる
- 初回 `onUpdate` で受けた件数と DOM 生成件数が一致する
- 初回描画後、先頭 item への `wb.focusThum(...)` が成功する

### 2. 描画更新

- `onUpdate` で受けた並び順どおりに `onCreateThum(mv, 1)` が走る
- 先頭再更新の `update` / `find` / `sort` / `addWhere` / `addOrder` / `addFilter` 後に、意図せず同じ DOM が積み増しされない
- full refresh 系操作では、必要に応じて `onClearAll` が先に効いていることを確認する

### 3. focus / select

- `onUpdate` 内の `wb.focusThum(mvs[0].id)` で `onSetFocus` が二重発火しない
- `multi-select : 1` 前提で、`onSetSelect` が選択状態へ追従する
- 外部から別 item へ focus が移った時、旧 id false -> 新 id true の順で返る

### 4. lifecycle

- `external -> external` の skin 切替で、旧 skin に `focus false -> select false -> clear -> leave` が返る
- `external -> built-in` の切替で、host 残骸を残さず抜ける
- DB 切替で、旧結果 DOM が残らない
- minimal reload でも `onSkinEnter` が余計に多重発火しない

### 5. scroll / seamless-scroll

- `scroll-id` 未指定でも `view` fallback で破綻しない
- `seamless-scroll : 2` の初回導線が `scrollSetting -> update -> onUpdate` で安定する
- 可視範囲外 item への `scrollTo` を後で足す時、`view` 基準で相対位置が破綻しない

### 6. 将来拡張の受け皿

- この fixture 自体は `onUpdateThum` を使っていないが、後で差分サムネ更新を足しても壊れないこと
- `onCreateThum` ベースの skin に対して `recordKey` / `thumbRevision` 契約をどう橋渡しするかを別途整理すること

## この fixture の崩れどころ

### 1. `onUpdate` が append 前提

- `TutorialCallbackGrid` 側の `onUpdate` 自体は clear せず、受けた item をそのまま `onCreateThum` へ流す
- そのため compat runtime 側で、先頭再更新 (`startIndex <= 0`) だけ `onClearAll` を先に返す設計にしている
- 今後 `startIndex > 0` の append を足す時は、この境界を崩さないこと

### 2. `focusThum` を callback 内から呼ぶ

- `onUpdate -> focusThum -> onSetFocus/onSetSelect` の再入がある
- ここで state 同期が甘いと、focus / select の二重発火や順序逆転が起きやすい

### 3. `scroll-id` なし

- `view` fallback が正しく動くことを前提にしている
- 今後 inner pane 構成の skin と同じ扱いにしてしまうと、scroll 対象を誤りやすい

## 次の実装順

1. `find / sort / addFilter` でも先頭再更新 clear が崩れないことを MainWindow fixture 視点まで確認済み
2. `external -> built-in` / minimal reload も実 fixture 視点まで確認済み
3. `WhiteBrowserDefaultList`、`WhiteBrowserDefaultGrid` / `Small` / `Big` への横展開は実 fixture 統合テストまで完了し、`TutorialCallbackGrid -> WhiteBrowserDefaultList` 切替、`TutorialCallbackGrid` 同一 fixture 再 navigate、MainWindow の DB切替 / `external -> external` / `external -> built-in` / minimal reload / `find / sort / addFilter` まで通ったので、次は手動受け入れ確認と大量件数対策へ寄せる

## 関連資料

- `WhiteBrowserSkin/Docs/攻略対象スキン一覧と使用連携コマンド_2026-04-11.md`
- `WhiteBrowserSkin/Docs/Progress_SkinFeature_2026-04-07.md`
- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
