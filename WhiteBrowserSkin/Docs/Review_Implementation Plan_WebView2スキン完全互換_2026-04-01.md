# レビュー: WebView2によるWhiteBrowserスキン完全互換 実装プラン 2026-04-01

## レビュー総評

計画の骨格は堅実。「既存 WPF タブの高速性を維持しつつ、外部スキンだけ WebView2 で動かす」という二刀流方針は正しい判断。フェーズ分割も最小成立ゴールから段階的に広げる構成で、破綻リスクが低い。

既存コード（`WhiteBrowserSkinCatalogService` / `WhiteBrowserSkinConfig` / `WhiteBrowserSkinDefinition` / `MainWindow.Skin.cs`）は「1から作り直しても良い」前提なので、WebView2 導入に合わせて責務を再整理する好機になる。

以下にプラン上の問題点・改善提案・追加すべき設計判断をまとめる。

---

## A. 方針レベルの評価

### A-1. 二刀流方針 ✅ 良い
- 既存 Default5 タブ → WPF 仮想化（高速）
- 外部 WB スキン → WebView2（互換重視）
- これが最も安全な構成。全スキン WebView2 化の不採用判断も正しい。

### A-2. フェーズ順序 ✅ 良い
- Phase 1（表示）→ Phase 2（API最小）→ Phase 3（コールバック）→ Phase 4（操作系）→ Phase 5（性能）
- 各フェーズに完了条件がある点も良い。

### A-3. セキュリティ方針 ✅ 良い
- `wb.writeFile` / `wb.execCmd` を後回しにする判断は正しい。
- ネットワークアクセス制限の方針も明記されている。

---

## B. 不足している設計判断

### B-1. ⚠️ prototype.js / wblib.js の提供方針が未定

WhiteBrowser スキンは `../prototype.js` と `../wblib.js` を `<script src>` で参照する。
この2ファイルをどう扱うか明記されていない。

**選択肢と推奨:**
1. **WhiteBrowser 同梱の prototype.js / wblib.js をそのまま skin フォルダに置く** → ライセンス問題あり（prototype.js は MIT だが wblib.js は WhiteBrowser 独自）
2. **wblib.js 互換レイヤーを自前で書き、`wb.*` API を WebView2 ブリッジ経由に差し替える** → 推奨。プランの Phase 2 ブリッジがまさにこれだが、`wblib.js` を置き換える具体的なインジェクション方式を決める必要がある
3. **ユーザーが WhiteBrowser から持ってくる前提** → 最もシンプルだが確認が必要

**推奨:** 選択肢 2 と 3 の併用。`skin/wblib.js` が無ければ自前互換版をインジェクトし、あれば元の wblib.js を読ませつつ `wb.*` を上書きする。

### B-2. ⚠️ WebView2 の初期化タイミングとライフサイクル

プラン 8 章リスクに「初期化コスト」とだけ書かれているが、具体策が無い。

**追加すべき設計判断:**
- WebView2 Environment はアプリ起動時に1つ作るか、スキン表示時に遅延生成するか
- UserDataFolder の配置場所（`%LOCALAPPDATA%\IndigoMovieManager\WebView2Cache` 等）
- スキン切替時に WebView2 を破棄して再作成するのか、`NavigateToString` で差し替えるのか
- DB 切替時のクリーンアップ手順

**推奨:** 遅延初期化（外部スキン選択時に初めて CoreWebView2 を生成）。UserDataFolder はログと同じ `%LOCALAPPDATA%` 配下に置く。スキン切替は Navigate で対応し、DB 切替時は `wb.clearAll` 相当を呼ぶ。

### B-3. ⚠️ Shift_JIS → UTF-8 変換

`DefaultGridWB.htm` は `charset=Shift_JIS` で書かれている。WhiteBrowser 本家スキンも Shift_JIS が多い。
WebView2 は UTF-8 優先なので、ローカルファイル読み込み時にエンコーディング問題が起きる。

**追加すべき設計判断:**
- `CoreWebView2.SetVirtualHostNameToFolderMapping` を使ってローカルファイルとして提供する場合、HTTP ヘッダで charset 指定できない
- 読み込み前に HTML を UTF-8 に変換してから `NavigateToString` で流すか、`SetVirtualHostNameToFolderMapping` で HTML の meta charset に任せるか

**推奨:** `NavigateToString` 方式を使い、C# 側で Shift_JIS → UTF-8 変換してから注入する。meta charset は書き換えるか除去する。

### B-4. ⚠️ WPF ↔ WebView2 の選択状態二重管理

プランのリスクに言及はあるが解決策が無い。

**追加すべき設計判断:**
- 「選択」の正本はどちら側に持つか
- WebView2 スキン表示中に WPF 側の `MainVM.SelectedMovieRecords` とどう同期するか
- 右クリックメニュー・ショートカットキーは WPF 側と WebView2 側のどちらでハンドルするか

**推奨:** 正本は常に WPF 側の ViewModel。WebView2 からの `wb.focusThum` / `wb.selectThum` は ViewModel を更新し、ViewModel の変更通知で WebView2 側も再描画する単方向フロー。右クリックメニューは WPF 側のメニューをオーバーレイし、WebView2 のコンテキストメニューは抑制する。

### B-5. ⚠️ サムネイルパスの提供方法

WebView2 からサムネイル画像を表示するには、サムネフォルダへのアクセスパスが必要。

**追加すべき設計判断:**
- `file:///` プロトコルで直接ローカルパスを渡すか
- `SetVirtualHostNameToFolderMapping` でサムネフォルダを仮想ホストにマップするか
- `wb.getThumDir` が返すパスの形式

**推奨:** `SetVirtualHostNameToFolderMapping` で `thum.local` のような仮想ホストにサムネフォルダをマップし、`<img src="https://thum.local/{hash}.jpg">` で参照させる。`file:///` は WebView2 のセキュリティポリシーで制限される場合がある。

---

## C. 既存コードの作り直しに関する提案

### C-1. 現行コードの評価

| ファイル | 状態 | 作り直し判断 |
|---------|------|------------|
| `WhiteBrowserSkinDefinition.cs` | イミュータブル DTO。シンプルで良い | **維持**。WebView2 用プロパティ（`RequiresWebView2` 等）を追加すれば十分 |
| `WhiteBrowserSkinConfig.cs` | config 値保持。必要十分 | **維持**。足りない config キーは段階的に追加 |
| `WhiteBrowserSkinCatalogService.cs` | 走査・パース・タブマップ | **部分作り直し**。`ResolvePreferredTabStateName` のタブマップロジックは WebView2 モードでは不要になるが、Default5 へのフォールバック用に残す価値あり |
| `MainWindow.Skin.cs` | スキン適用・永続化 | **作り直し**。WebView2 host の出し入れ、表示モード切替の責務が増えるため、`MainWindow` の partial ではなく独立サービスに移す方が良い |

### C-2. 新規追加ファイル案（プランの 6.1 を具体化）

```
skin/
├── Host/
│   └── WhiteBrowserSkinHostControl.xaml(.cs)    ← WebView2 表示ホスト UserControl
├── Runtime/
│   ├── WhiteBrowserSkinRuntimeBridge.cs         ← JS ↔ C# メッセージブリッジ
│   ├── WhiteBrowserSkinApiService.cs            ← wb.* API 実装本体
│   └── WhiteBrowserSkinRenderCoordinator.cs     ← 更新タイミング制御
├── Compat/
│   └── wblib-compat.js                          ← wblib.js 互換レイヤー（自前）
├── WhiteBrowserSkinCatalogService.cs            ← 既存（微修正）
├── WhiteBrowserSkinConfig.cs                    ← 既存（維持）
├── WhiteBrowserSkinDefinition.cs                ← 既存（プロパティ追加）
└── WhiteBrowserSkinOrchestrator.cs              ← MainWindow.Skin.cs から独立化
```

---

## D. プランへの具体的修正提案

### D-1. Phase 1 に追加すべき作業

- [ ] `Microsoft.Web.WebView2` パッケージバージョン選定（現時点で安定版 `1.0.2739.15` 以降推奨）
- [ ] WebView2 Runtime の有無チェックとユーザーへの案内（Runtime 未インストール時のフォールバック）
- [ ] UserDataFolder のパス決定
- [ ] Shift_JIS 対応方針の確定
- [ ] `wblib.js` 互換版のスケルトン作成
- [ ] スキン HTML 読み込み方式の確定（`NavigateToString` vs `SetVirtualHostNameToFolderMapping`）

### D-2. Phase 2 の設計補足

- `window.chrome.webview.postMessage` 方式は非同期なので、`wb.getInfo` のような同期戻り値を期待する API は Promise ベースに変換する必要がある
- WhiteBrowser の元 API は同期呼び出しなので、`wblib-compat.js` 側で `async/await` ラッパーを被せるか、`AddHostObjectToScript` で同期オブジェクトを公開するか決める必要がある

**推奨:** `AddHostObjectToScript` は COM 互換が必要で重いため、`postMessage` + Promise に統一し、`wblib-compat.js` で `wb.getInfo = async function(id) { ... }` として提供する。既存スキンが `var info = wb.getInfo(id)` で同期呼び出ししている場合は完全互換にならないが、大半のスキンは `onUpdate` コールバック内で結果を受け取る構造なので、実用上問題は少ない。

### D-3. Phase 5 に追加すべき観点

- WebView2 プロセスのメモリ使用量監視（Chromium 子プロセスが重い）
- スキン表示中に WPF タブへ戻った際の WebView2 一時非表示（`Visibility.Collapsed` でも描画コストが発生しうる点の対策）
- 数万件規模では DOM 生成自体が問題になるため、仮想スクロールの具体的な実装方式を決める（IntersectionObserver + ページャ等）

---

## E. セキュリティ補足

### E-1. WebView2 の設定で制限すべき項目
```csharp
// Phase 1 で最初に設定する推奨値
settings.IsScriptEnabled = true;              // スキン JS は必要
settings.AreDefaultScriptDialogsEnabled = false; // alert/confirm 抑制
settings.IsWebMessageEnabled = true;           // ブリッジ用
settings.AreDevToolsEnabled = false;           // リリース時は無効
settings.IsStatusBarEnabled = false;
settings.AreDefaultContextMenusEnabled = false; // WPF 側メニューを使う
settings.AreBrowserAcceleratorKeysEnabled = false; // Ctrl+R 等を抑制
```

### E-2. ナビゲーション制限
- `NavigationStarting` イベントで外部 URL へのナビゲーションをブロックする
- `NewWindowRequested` も同様にブロック
- `WebResourceRequested` でローカルファイル以外のリソース取得を制限（Phase 1 時点）

---

## F. リスク追加

プラン 8 章に追加すべきリスク:

| リスク | 影響 | 対策案 |
|--------|------|--------|
| WebView2 Runtime 未インストール | スキン表示不能 | 検出時にメッセージ表示、Evergreen Runtime 案内 |
| prototype.js 互換性 | prototype.js は古い（v1.7系）。現代ブラウザの標準 API と衝突する可能性 | Chromium 上での動作確認を Phase 1 に含める |
| Shift_JIS HTML | 文字化け | C# 側で変換してから注入 |
| wb.* API の同期/非同期差異 | 既存スキン JS が同期戻り値を前提にしている場合動かない | 主要スキンでの動作確認リストを作る |
| Chromium プロセスのメモリ消費 | 低スペック環境で圧迫 | WebView2 を1インスタンスに制限、非表示時は破棄を検討 |

---

## G. 総合判断

| 事項 | 判断 |
|------|------|
| プランの方向性 | ✅ 承認。WebView2 二刀流は正しい |
| フェーズ分割 | ✅ 承認。順序も妥当 |
| 最小成立ゴール | ✅ 承認。実用性と安全性のバランスが取れている |
| 設計要点 | ⚠️ 補足が必要（B-1 〜 B-5 の判断を Phase 1 着手前に確定すべき） |
| 既存コード作り直し | ⚠️ `MainWindow.Skin.cs` は独立サービスに移すべき。他3ファイルは拡張で対応可 |
| セキュリティ | ⚠️ E-1, E-2 をプランに明記すべき |
| リスク | ⚠️ F 章の追加リスクをプランに反映すべき |

**結論: Phase 1 着手前に B-1 〜 B-5 の設計判断を確定し、プランに反映してから実装を開始すべき。**
