# Implementation Plan: WebView2によるWhiteBrowserスキン完全互換 2026-04-01

最終更新日: 2026-04-01

変更概要:
- レビューで評価された「二刀流方針」「段階導入」「危険 API 後回し」を維持した
- 実装前に固定すべき判断を `Phase 0` として新設した
- `prototype.js` / `wblib.js`、WebView2 ライフサイクル、Shift_JIS 対応、選択状態の正本、サムネ参照方式を明文化した
- `dbIdentity` の具体実装、`recordKey`、`thumbRevision`、寸法 DTO 必須化を本体計画書へ反映した
- `MainWindow.Skin.cs` 直結案を見直し、Orchestrator 分離を前提に再構成した
- セキュリティ、性能観測、Runtime 未導入時のフォールバック、テスト方針を追加した
- Phase 1 着手前に固定すべき 5 項目を、推奨案ベースの確定事項として明示した

## 1. レビューで特に良かった点
- 既存 Default5 タブを WPF 仮想化で維持し、外部 WB スキンだけを WebView2 へ逃がす二刀流方針は正しい
- `Phase 1 -> 2 -> 3 -> 4 -> 5` の順序は破綻しにくく、実装の足場として良い
- `wb.writeFile` / `wb.execCmd` のような危険 API を初期段階から抱え込まない判断は堅実
- `WhiteBrowserSkinDefinition` / `WhiteBrowserSkinConfig` を活かしつつ、`MainWindow.Skin.cs` だけは独立サービスへ逃がす、という整理は筋が良い

この計画では、上の良い部分を壊さずに、未確定だった設計判断を先に固定する。

## 1.1 Phase 1 着手前に固定する 5 決定

| # | 項目 | この計画で確定する内容 |
|---|---|---|
| 1 | `wblib.js` の提供方式 | WhiteBrowser 由来の `wblib.js` 再配布前提にしない。自前 `wblib-compat.js` を用意し、HTML 前処理で差し替える |
| 2 | WebView2 初期化 / UserDataFolder | 外部スキン初回選択時に遅延初期化。UserDataFolder は `%LOCALAPPDATA%\IndigoMovieManager\WebView2Cache\` |
| 3 | Shift_JIS HTML の変換方式 | C# 側でバイト読込 -> 文字コード判定 -> UTF-8 正規化 -> `NavigateToString` 注入 |
| 4 | 選択状態の正本 | 常に WPF 側 ViewModel を正本とする。WebView2 側は要求と描画に徹する |
| 5 | サムネイルパスの提供方法 | `file:///` を使わず、`thum.local` 仮想ホスト経由で提供する |

この 5 項目は、Phase 1 に入る前提条件ではなく、この計画時点で固定済みの設計判断として扱う。

## 2. 目的
- WhiteBrowser のスキン資産を、できるだけそのまま IndigoMovieManager で動かせるようにする
- `HTML + CSS + JavaScript + wb.* API + callback` を含む「実行環境」までを互換対象にする
- ただし、このブランチの最優先であるユーザー体感テンポは壊さない

## 3. 現状

### 3.1 既にできていること
- `skin` フォルダの走査
- `skin\<SkinName>\<SkinName>.htm/.html` の検出
- `div#config` の読取
- `system.skin` への外部スキン名保存
- `profile` テーブルへの外部スキン別 `LastUpperTab` 保存
- 設定画面から現在DBのスキン選択

### 3.2 まだ足りないこと
- HTML 自体の表示
- `prototype.js` / `wblib.js` 実行
- `wb.update`, `wb.getInfo`, `wb.changeSkin` などの API
- `onUpdate`, `onCreateThum`, `onSetFocus`, `onSetSelect`, `onSkinEnter`, `onSkinLeave`
- DOM ベースのシームレススクロール
- 大量件数時の仮想化

## 4. 採用方針

### 4.1 画面方針
- WhiteBrowser 完全互換を目指す描画エンジンは `WebView2` を採用する
- 既存 `DefaultSmall / DefaultBig / DefaultGrid / DefaultList / DefaultBig10` は従来の WPF 仮想化表示を維持する
- 外部 WhiteBrowser 互換スキンだけを WebView2 モードで動かす

### 4.2 理由
- WhiteBrowser スキンは単なるレイアウト定義ではなく、`HTML + JS 実行環境` そのものだから
- WPF 単独で完全互換を狙うと、HTML テンプレート解釈器と `wb.*` ランタイムを別実装することになり、重く壊れやすい
- WebView2 なら描画基盤を Chromium へ任せ、こちらは API ブリッジと同期制御に集中できる

### 4.3 非採用案
- 全スキン WebView2 化
  - 不採用。既存標準タブの高速性を失うリスクが高い
- WPF 単独完全互換
  - 不採用。再実装量が大きすぎる

## 5. 互換対象の優先順位

### 5.1 最優先で互換を取るもの
- `テンプレート2` 系の標準的な WB スキン
- `wb.update` と `onUpdate` を中心に描画するスキン
- `focus/select` 同期、スキン切替、プロフィール保存

### 5.2 次点で互換を取るもの
- 独自の検索 UI
- 独自のタグ UI
- `wb.find`, `wb.sort`, `wb.addTag`, `wb.flipTag` を使うスキン

### 5.3 初期段階では保証しないもの
- 同期戻り値を強く前提にする複雑な `wb.getInfo` 利用
- 外部 URL ナビゲーション
- 任意ファイル書込や外部コマンド実行
- IE 時代の細かな DOM 挙動差まで含む厳密互換

## 6. Phase 0: 実装前に固定した設計判断

### 6.1 `prototype.js` / `wblib.js` 方針
- `wblib.js` は WhiteBrowser 由来の実ファイルを再配布前提にしない
- 自前の `wblib-compat.js` を用意し、HTML 前処理で元の `wblib.js` 参照を差し替える
- `prototype.js` は次の順で解決する
  - スキン側に同梱されていればそれを使う
  - 同梱が無い場合は、ライセンス確認済みの互換版をプロジェクト側で提供する
  - それも無い場合は、起動時に警告を出し、互換モードを拒否または最低限モードへ落とす

### 6.2 WebView2 初期化とライフサイクル
- CoreWebView2 Environment は外部スキンが初めて選ばれた時に遅延生成する
- UserDataFolder は `%LOCALAPPDATA%\IndigoMovieManager\WebView2Cache\` 配下へ置く
- WebView2 インスタンスは原則 1 つに絞る
- スキン切替は基本的に `NavigateToString` で差し替える
- DB 切替時は `wb.clearAll` 相当を呼んだ上で状態をリセットし、インスタンスは使い回す
- 長時間非表示が続く場合だけ破棄候補にする

### 6.3 HTML 読み込みと Shift_JIS 対応
- スキン HTML は C# 側でバイト列として読み込む
- BOM 判定が無い場合は `meta charset` を見て判定し、未指定時は Shift_JIS を優先候補にする
- 読み込み後は UTF-8 文字列へ正規化し、`NavigateToString` で流す
- 元の `meta charset` は UTF-8 前提へ置換または除去する
- 相対パス崩れを防ぐため、HTML へ `<base href="https://skin.local/">` を注入する

### 6.4 ローカル資産とサムネイルの参照方式
- スキンフォルダは `SetVirtualHostNameToFolderMapping("skin.local", ...)` で公開する
- サムネイルルートは `SetVirtualHostNameToFolderMapping("thum.local", ...)` で公開する
- スキン HTML / CSS / JS / 画像は `https://skin.local/...`
- サムネイルは `https://thum.local/...`
- `file:///` ベースの直接参照は採用しない

### 6.5 選択状態の正本
- 選択状態の正本は常に WPF 側 ViewModel とする
- WebView2 側の `wb.focusThum` / `wb.selectThum` は ViewModel 更新要求として扱う
- ViewModel 変更通知を契機に WebView2 側を再同期する
- WebView 側を正本にしない

### 6.6 ショートカットと右クリック
- 既存ショートカットは WPF 側を正本として維持する
- WebView2 の既定コンテキストメニューは無効化する
- 右クリックメニューは WPF 側の既存メニューをオーバーレイして出す

### 6.7 Runtime 未導入時のフォールバック
- WebView2 Runtime が無い場合、外部スキン表示へ入る前に検出する
- 検出時は一度だけユーザーへ案内を出す
- 表示自体は外部スキン名を保持したまま、既存の `PreferredTabStateName` へ一時フォールバックする
- DB の `system.skin` は勝手に built-in 名へ上書きしない

### 6.8 DTO の識別子とサムネ更新契約
- `dbIdentity` は **現在の MainDB 正規化フルパスを元にした安定ハッシュ** を正式採用する
- 具体実装は `NormalizeMainDbPath(DBFullPath)` 相当の正規化結果を UTF-8 化し、SHA-256 の hex 文字列へ変換する
- `movieId` は DB 内ローカル ID として保持し、更新主キーは **`recordKey = "{dbIdentity}:{movieId}"`** とする
- `thumbRevision` はサムネ改訂番号として必須採用し、`thumbUrl` は常に `?rev={thumbRevision}` を付けた最終 URL として返す
- `thumbNaturalWidth` / `thumbNaturalHeight` / `thumbSheetColumns` / `thumbSheetRows` は v1 DTO から必須とする
- `onUpdateThum` 系更新通知も `recordKey` と `thumbRevision` を必須で含める

この判断により、

- DB 切替や再登録をまたいでも、別 DB の `movieId` 衝突で誤更新しない
- WebView2 側で古い画像キャッシュが残っても、`thumbUrl?rev=` と `thumbRevision` 比較の両方で追い出せる
- 寸法情報を後付けオプションにせず、v1 から表示契約として固定できる

Phase 0 の判断は本計画で固定済みとし、この内容を前提に Phase 1 実装へ進む。

## 7. 実装ゴール

### 7.1 最終ゴール
- WhiteBrowser 形式のスキンフォルダを置くと、そのスキンを IndigoMovieManager で選択して表示できる
- `wb.*` API の主要機能が動く
- 既存 DB・検索・選択・タグ編集・再生・サムネイル作成と連動する
- 大量件数でも UI が詰まりにくい

### 7.2 最小成立ゴール
- `WebView2` 上で外部スキン HTML を表示できる
- `div#view` を見つけて、検索結果の最小一覧を注入できる
- `wb.update`, `wb.getInfo`, `wb.focusThum`, `wb.getSkinName`, `wb.getThumDir` が動く
- `onUpdate` と `onSetFocus` だけ先に発火できる

## 8. ランタイム構成

### 8.1 ファイル構成案
```text
skin/
├── Host/
│   └── WhiteBrowserSkinHostControl.xaml(.cs)
├── Runtime/
│   ├── WhiteBrowserSkinRuntimeBridge.cs
│   ├── WhiteBrowserSkinApiService.cs
│   ├── WhiteBrowserSkinRenderCoordinator.cs
│   └── WhiteBrowserSkinEncodingNormalizer.cs
├── Compat/
│   ├── wblib-compat.js
│   └── prototype.js
├── WhiteBrowserSkinCatalogService.cs
├── WhiteBrowserSkinConfig.cs
├── WhiteBrowserSkinDefinition.cs
└── WhiteBrowserSkinOrchestrator.cs
```

### 8.2 責務
- `WhiteBrowserSkinHostControl`
  - WebView2 を持つ表示ホスト
- `WhiteBrowserSkinRuntimeBridge`
  - JS と C# のメッセージ往復
- `WhiteBrowserSkinApiService`
  - `wb.*` API 実装本体
- `WhiteBrowserSkinRenderCoordinator`
  - 更新タイミング、差分反映、可視範囲制御
- `WhiteBrowserSkinEncodingNormalizer`
  - Shift_JIS / UTF-8 正規化
- `WhiteBrowserSkinOrchestrator`
  - MainWindow から独立した適用・切替・永続化の司令塔

### 8.3 既存コードの扱い
- `WhiteBrowserSkinDefinition.cs`
  - 維持。必要に応じて `RequiresWebView2` などを追加
- `WhiteBrowserSkinConfig.cs`
  - 維持。足りない config キーを段階追加
- `WhiteBrowserSkinCatalogService.cs`
  - 維持しつつ、HTML 前処理と互換判定の入口を拡張
- `MainWindow.Skin.cs`
  - 直持ちはやめ、`WhiteBrowserSkinOrchestrator` へ責務を移す

## 9. データと同期の原則

### 9.1 DTO
- JS へは `MovieRecords` をそのまま渡さない
- スキン向け DTO を切る
- v1 初期項目
  - `dbIdentity`
  - `movieId`
  - `recordKey`
  - `movieName`
  - `moviePath`
  - `thumbUrl`
  - `thumbRevision`
  - `thumbSourceKind`
  - `thumbNaturalWidth`
  - `thumbNaturalHeight`
  - `thumbSheetColumns`
  - `thumbSheetRows`
  - `length`
  - `size`
  - `tags`
  - `score`
  - `exists`
  - `selected`

`id` のような曖昧な単独識別子は使わず、DTO の正式主キーは `recordKey` とする。

### 9.2 同期方式
- WebView2 からの操作は「要求」
- WPF ViewModel の変更が「確定」
- 確定した状態を WebView2 側へ再通知する単方向フローにする
- サムネ更新は `recordKey` 単位で扱い、`thumbRevision` が変わった時だけ画像差し替えを行う

### 9.3 API の同期 / 非同期差
- `window.chrome.webview.postMessage` は非同期
- 初期実装では `wb.*` は Promise ベース互換へ寄せる
- `wblib-compat.js` 側で async ラッパーを提供する
- 同期戻り値を強く前提にしたスキンは、互換ランクを下げて扱う

## 10. フェーズ分割

## Phase 1: WebView2ホスト導入

### 10.1 目的
- 外部スキンだけを表示する `WebView2` ベースの host を追加する

### 10.2 作業
- `Microsoft.Web.WebView2` パッケージ導入
- Runtime 有無チェックと案内導線追加
- UserDataFolder の配置確定
- `WhiteBrowserSkinHostControl` 追加
- スキン HTML の文字コード正規化
- `<base href>` 注入
- `skin.local` / `thum.local` のホストマッピング
- `wblib-compat.js` のスケルトン作成
- 外部スキン時だけ WebView2 host を出す分岐追加

### 10.3 完了条件
- 指定した外部スキン HTML が WebView2 内で表示される
- Shift_JIS スキンが文字化けしない
- Runtime 未導入時に安全フォールバックする
- 既存 WPF タブには影響しない
- Phase 0 の 5 決定をひっくり返す追加設計変更が発生していない

## Phase 2: JS ブリッジ最小導入

### 10.4 目的
- `wb.*` API の最小セットを JavaScript から呼べるようにする

### 10.5 作業
- `window.chrome.webview` ベースのメッセージブリッジ導入
- `WhiteBrowserSkinRuntimeBridge` 追加
- `WhiteBrowserSkinApiService` 追加
- `dbIdentity` / `recordKey` / `thumbRevision` を含む v1 DTO 契約を固定
- 最初に実装する API
  - `wb.update`
  - `wb.getInfo`
  - `wb.getInfos`
  - `wb.focusThum`
  - `wb.getSkinName`
  - `wb.getDBName`
  - `wb.getThumDir`
  - `wb.trace`

### 10.6 完了条件
- スキン HTML 側の JS から `wb.update()` を呼ぶと、`recordKey` と `thumbUrl?rev=` を含む一覧データが返る
- `wb.getInfo()` / `wb.getInfos()` が `dbIdentity`、`thumbRevision`、寸法 DTO を返せる
- `wb.focusThum()` で選択状態が同期する
- `wb.getThumDir()` が `thum.local` ベースの参照先を返せる

## Phase 3: コールバック互換

### 10.7 目的
- WhiteBrowser スキンの主要 callback を動かす

### 10.8 作業
- `onUpdate`
- `onClearAll`
- `onSetFocus`
- `onSetSelect`
- `onUpdateThum`
- `onSkinEnter`
- `onSkinLeave`

`onUpdateThum` の v1 契約は、少なくとも次を含む前提で実装する。

- `recordKey`
- `thumbUrl`
- `thumbRevision`
- `thumbSourceKind`
- `thumbNaturalWidth`
- `thumbNaturalHeight`
- `thumbSheetColumns`
- `thumbSheetRows`

### 10.9 完了条件
- スキン側の一覧再描画ロジックが callback ベースで成立する
- `onUpdateThum` で対象レコードだけを `recordKey` 単位に安全更新できる
- `thumbRevision` 更新で古い画像キャッシュが残らない
- スキン切替と DB 切替で enter / leave が破綻しない

## Phase 4: 操作系 API 拡張

### 10.10 目的
- WhiteBrowser 的な操作を実用レベルにする

### 10.11 作業
- `wb.find`
- `wb.sort`
- `wb.addWhere`
- `wb.addOrder`
- `wb.addTag`
- `wb.removeTag`
- `wb.flipTag`
- `wb.selectThum`
- `wb.scrollTo`
- `wb.writeProfile`
- `wb.getProfile`
- `wb.changeSkin`

### 10.12 完了条件
- スキン側 UI から検索・タグ・選択・スキン切替が成立する
- `profile` 連携が WPF 側永続化と競合しない

## Phase 5: パフォーマンス対策

### 10.13 目的
- 大量件数時でも体感テンポを落とさない

### 10.14 作業
- 全件 HTML 再生成をやめ、差分更新またはページ単位投入へ寄せる
- JS 側に仮想スクロール導入
- サムネイルは可視範囲優先ロード
- `getInfos` の返却件数制御
- 大量時は `onUpdate` へ一括全件ではなくページ単位投入
- WebView2 側の画像 decode と WPF 側サムネ管理の責務整理
- Chromium 子プロセスのメモリ使用量観測
- WPF 側へ戻った時の非表示 / 一時停止 / 破棄条件の整理

### 10.15 完了条件
- 数千件規模でも初動が許容範囲
- 通常タブより極端に遅くならない
- 非表示時に WebView2 が無駄に走り続けない

## 11. セキュリティ

### 11.1 初期設定
```csharp
settings.IsScriptEnabled = true;
settings.AreDefaultScriptDialogsEnabled = false;
settings.IsWebMessageEnabled = true;
settings.AreDevToolsEnabled = false;
settings.IsStatusBarEnabled = false;
settings.AreDefaultContextMenusEnabled = false;
settings.AreBrowserAcceleratorKeysEnabled = false;
```

### 11.2 ナビゲーション制限
- `NavigationStarting` で外部 URL ナビゲーションをブロックする
- `NewWindowRequested` で新規ウィンドウをブロックする
- `WebResourceRequested` で `skin.local` / `thum.local` 以外への取得を初期段階では遮断する

### 11.3 危険 API
- `wb.writeFile`, `wb.execCmd` は初期フェーズへ入れない
- 実装する場合も、ユーザー明示操作と許可範囲チェックを前提にする

## 12. リスクと対策

| リスク | 影響 | 対策 |
|---|---|---|
| WebView2 Runtime 未導入 | 外部スキン表示不能 | 起動前検出、案内表示、一時フォールバック |
| `prototype.js` 互換差 | スキン JS の一部不動作 | 実スキンでの検証リストを持つ |
| Shift_JIS HTML | 文字化け | C# 側で UTF-8 正規化して注入 |
| `wb.*` の同期 / 非同期差 | 一部スキンが動かない | Promise ベース compat と互換ランク管理 |
| 大量 DOM | 初動遅延・メモリ圧迫 | 差分更新、仮想スクロール、ページ単位投入 |
| WPF / WebView 選択二重管理 | 選択ずれ | ViewModel 正本の単方向同期 |
| Chromium 子プロセスの常駐 | 低スペック環境で重い | 単一インスタンス、非表示時の抑制、観測導入 |

## 13. テスト方針

### 13.1 ロジックテスト
- Config パース
- 文字コード判定と UTF-8 正規化
- HTML 前処理
- Skin 切替オーケストレーション
- `wb.*` API のメッセージ解決

### 13.2 結合テスト
- 外部スキン選択 -> 表示
- DB 切替 -> `onClearAll` / `onSkinEnter` / `onSkinLeave`
- 検索 -> `wb.update`
- 選択変更 -> `wb.focusThum`
- サムネ更新 -> `onUpdateThum`
- Runtime 未導入 -> フォールバック

### 13.3 体感確認
- 標準 WPF タブの初動が悪化していない
- 外部スキンで 100 / 1000 / 5000 件規模の初動を比較できる
- DB 切替で残像や古い選択状態が残らない

## 14. 受け入れ条件
- 外部 WhiteBrowser スキンを選択して一覧表示できる
- スキン切替でクラッシュしない
- 既存標準タブのテンポを壊さない
- DB の `system.skin` / `profile` と整合する
- 少なくとも `wb.update` 系の基本フローが動く
- Runtime 未導入時に安全に戻れる

## 15. 最初の実装順
1. `WhiteBrowserSkinOrchestrator` を先に切り出す
2. `WebView2` host と文字コード正規化を入れる
3. `wblib-compat.js` と `wb.update` / `onUpdate` を最初に成立させる
4. `focus/select` 同期を入れる
5. `profile` 永続化を JS API とつなぐ
6. 仮想スクロールと差分更新へ進む

## 16. 今回の結論
- WhiteBrowser 完全互換を本気でやるなら、`WebView2` 採用が最有力
- ただし必要なのは `WebView2` 単体ではなく、`wb.*` 互換ランタイム一式
- 既存 WPF 高速タブは維持し、外部スキンだけ WebView2 へ逃がすハイブリッド構成が最も安全
- 手戻りを生みやすい 5 項目は本計画で固定済みなので、この前提で Phase 1 へ着手してよい
