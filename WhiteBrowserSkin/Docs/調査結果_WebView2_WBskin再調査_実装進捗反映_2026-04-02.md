# 調査結果 WebView2 / WB skin 再調査 実装進捗反映 2026-04-02

最終更新日: 2026-04-02

変更概要:
- 2026-04-01 時点の WebView2 / WB skin 計画と、2026-04-02 までに進んだ実装を突き合わせた
- 「構想段階の論点」と「もう実装済みの範囲」を分け直した
- 次に着手すべき本命を、現状コード基準で整理した

## 1. 目的

WebView2 による WhiteBrowser 互換 skin 対応は、4/1 の計画以降に実装が前進している。

そのため、前回の調査結果をそのまま読むと、

- まだ未着手だと思ってしまう点
- 逆に、まだ本当に未解決な点

が混ざって見えやすい。

本書は、2026-04-02 時点の現状を改めて固定するための再調査メモである。

## 2. 結論

結論は次である。

1. WebView2 外部 skin は、もう「構想だけ」ではなく実アプリ起動で成立している
2. `wb.update / getInfo / getInfos / find / focusThum` までの最小 bridge は入っている
3. サムネ契約も、以前の「統合待ち」から前進し、`thumbUrl / thumbRevision / sourceKind / 寸法 DTO` まで本体実装済みである
4. したがって次の本命は「host 起動可否」ではなく、「WB callback 互換の厚み」と「操作 API 拡張」である
5. 特に `onCreateThum / onSetSelect / onUpdateThum / onSkinEnter / onSkinLeave` と `wb.sort / profile / tag 系` が次の山である

## 3. 前回調査から前進した点

### 3.1 実アプリで外部 skin が起動する

次は確認済みである。

- `Views/Main/MainWindow.WebViewSkin.cs`
  - host を `Hidden` で仮マウントしてから WebView2 初期化する
  - refresh scheduler で skin / DB 切替の揺れを畳む
- `WhiteBrowserSkin/Host/WhiteBrowserSkinHostControl.xaml.cs`
  - host control が runtime bridge と render coordinator を抱える
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
  - 実機での成功条件と失敗時の切り分けが整理済み

これは、4/1 時点の「Phase 1 の目標」から一段前へ進んでいる。

### 3.2 host chrome 最小化まで入っている

- `Views/Main/MainWindow.WebViewSkin.Chrome.cs`
- `WhiteBrowserSkin/Docs/Implementation Note_HostChromeMinimal初期実装_2026-04-02.md`

外部 skin 表示中は、重い既存 header をそのまま見せず、

- DB 名
- skin 名
- `再読込`
- `Gridへ戻る`
- `設定`

だけを持つ minimal chrome に切り替える構成まで入っている。

### 3.3 最小 `wb.*` bridge はもうある

- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinRuntimeBridge.cs`
  - `postMessage` transport
  - `skin.local` / `thum.local`
  - `WebResourceRequested` でサムネ実体を返す
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinApiService.cs`
  - `update`
  - `find`
  - `getInfo`
  - `getInfos`
  - `getSkinName`
  - `getDBName`
  - `getThumDir`
  - `trace`
  - `focusThum`
- `Views/Main/MainWindow.WebViewSkin.Api.cs`
  - MainWindow 側の API bridge 配線
- `skin/Compat/wblib-compat.js`
  - Promise ベースの `wb.*`
  - `onUpdate`
  - `onSetFocus`
  の最小 callback dispatch

つまり「HTML を出すだけ」の段階は既に越えている。

### 3.4 検索 bridge も最小成立している

- `Views/Main/Docs/Implementation Note_スキン検索bridge最小実装_2026-04-02.md`
- `Views/Main/Docs/Implementation Plan_検索機能統合_タグ検索とスキン検索UI対応_2026-04-02.md`

`wb.find(...)` から本体検索へ入る最小経路は実装済みである。

したがって、検索 UI を持つ skin は「最低限の検索」はもう試せる段階に入った。

### 3.5 サムネ契約はもう「統合待ちだけ」ではない

4/1 の handoff では、

- `thumbUrl`
- `thumbRevision`
- source image fallback
- 寸法 DTO

は統合待ちの色が強かった。

しかし 4/2 時点では次が入っている。

- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinThumbnailContractService.cs`
  - `dbIdentity`
  - `recordKey`
  - `thumbUrl`
  - `thumbRevision`
  - `thumbSourceKind`
  - `thumbNaturalWidth / Height`
  - `thumbSheetColumns / Rows`
  の正本 service
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinThumbnailContracts.cs`
  - `thum.local` codec
  - `__external` route
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinRuntimeBridge.cs`
  - `thum.local` 実レスポンス返却
  - 外部サムネ path の許可制御
- `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinThumbnailContractServiceTests.cs`
  - `source-image-direct`
  - `source-image-imported`
  - `error-placeholder`
  - `missing-file-placeholder`
  を確認
- `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinRuntimeBridgeIntegrationTests.cs`
  - 200 / 403 / 404 とヘッダー確認

ここは、前回調査からかなり前進した点である。

## 4. 現在地

### 4.1 もうできていること

- 外部 skin の catalog / config 読み込み
- `system.skin` / `profile` を使った skin 永続化
- 実アプリでの WebView2 host 表示
- runtime 未導入時の fallback 分岐
- minimal host chrome
- `wb.update / find / getInfo / getInfos / focusThum`
- `thum.local` による managed / external / placeholder サムネ配信
- `thumbRevision` 付き URL
- sample skin の UTF-8 整理
- 実機ログによる切り分け

### 4.2 まだ未完のもの

#### A. callback 互換はまだ薄い

`wblib-compat.js` で実際に callback 連携があるのは主に

- `onUpdate`
- `onSetFocus`

までである。

WhiteBrowser 互換として本当に厚みが欲しいのは次だが、ここはまだ本命未着手に近い。

- `onCreateThum`
- `onClearAll`
- `onSetSelect`
- `onUpdateThum`
- `onSkinEnter`
- `onSkinLeave`

#### B. 操作 API はまだ最小セット

未着手または本格未着手の中心は次。

- `wb.sort`
- `wb.writeProfile`
- `wb.getProfile`
- `wb.changeSkin`
- tag 系 API
  - `wb.addTag`
  - `wb.removeTag`
  - `wb.flipTag`

#### C. WB 的な callback 形状にはまだ差がある

例えば `focusThum` は bridge としては通るが、
WhiteBrowser 既存 skin が期待する「昔の callback 引数形」と完全一致しているとはまだ言いにくい。

このため「最小 sample は動く」が「既存の普及 skin をそのまま広く受ける」段階にはまだ至っていない。

#### D. 大量件数での本命対策はこれから

4/1 の計画にある

- 差分更新
- 仮想スクロール
- 大量 DOM 抑制
- 可視範囲優先

は、まだ本格着手前である。

今は「成立確認」と「最小互換」が先に進んでいる段階である。

## 5. 実装上の見立て

### 5.1 「WebView2が出ない」は本命ではなくなった

4/1 時点ではここが最大論点だったが、
4/2 時点では

- host 仮マウント
- refresh 明示
- 実機ログ

が揃ったので、ここは「不安定な未踏領域」ではなくなった。

### 5.2 いまのボトルネックは WB 互換の厚み

外部 skin を広く受けるための本当の残件は、

- callback 群の互換
- 操作 API
- 既存 skin が期待する呼び順

である。

つまり次の仕事は「host を出す」ではなく、
「古い WB skin の JavaScript がどこまで無修正で動くか」を押し上げる側へ移っている。

### 5.3 サムネ契約は “別班待ち” から “接続済み” へ変わった

以前の handoff で統合待ち扱いだったサムネ契約は、いまは

- DTO
- URL codec
- revision
- runtime bridge 応答
- テスト

まで入っている。

したがって次は「契約を作る」より、
`onUpdateThum` や skin DOM 差分更新へどう食わせるかが主題である。

## 6. 次の本命候補

優先順のおすすめは次である。

1. `onCreateThum` 互換
2. `onSetSelect` / `onClearAll` / `onSkinEnter` / `onSkinLeave`
3. `wb.sort`
4. `wb.getProfile / writeProfile / changeSkin`
5. tag 系 API
6. 大量件数対策

### 6.1 まず `onCreateThum`

理由:

- 既存 WB skin fixture でも使っている
- 「一覧が出る」から「既存 skin が描ける」へ一段進める
- `thumbUrl / sizeInfo / sourceKind` がもうあるので、今が一番つなぎやすい

### 6.2 その次に lifecycle callback

理由:

- skin 切替や DB 切替の見た目整合に直結する
- 実アプリで既に host refresh 基盤があるので、イベント発火点を増やしやすい

### 6.3 操作 API は callback 互換の後

理由:

- 先に callback を厚くしないと、既存 skin を動かした時のデバッグがしにくい
- `sort / profile / tag` は UI 上の機能としては大事だが、互換の土台ほど根幹ではない

## 7. 今やらない方がよいこと

1. 既存 WPF built-in 5 タブを急いで WebView2 化すること
2. 大量 DOM 最適化を callback 互換より先に進めること
3. `MainWindow.xaml.cs` に WebView skin 連携を戻すこと
4. `file:///` に逃げること

## 8. 再調査結論

2026-04-02 時点の WebView2 / WB skin は、

- host 表示
- bridge 最小セット
- サムネ契約
- 検索 bridge

まで進んでいる。

したがって、今後の論点は

- 起動するか
- URL をどうするか

ではなく、

- WB callback をどこまで厚くするか
- 既存 skin をどこまで無修正で受けるか

に移っている。

次の本命は `onCreateThum` を先頭にした callback 互換強化でよい。

## 9. 参照

- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `WhiteBrowserSkin/Docs/WhiteBrowser_SkinResearch_2026-04-01.md`
- `WhiteBrowserSkin/Docs/PM_Handoff_WebView2P2_WebView側フック点_2026-04-01.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2サムネ契約土台実装_2026-04-01.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
- `WhiteBrowserSkin/Docs/Implementation Note_HostChromeMinimal初期実装_2026-04-02.md`
- `WhiteBrowserSkin/Docs/PM_Handoff_外部スキン一時休止_2026-04-02.md`
- `Views/Main/Docs/Implementation Note_スキン検索bridge最小実装_2026-04-02.md`
