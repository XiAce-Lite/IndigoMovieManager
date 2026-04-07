# スキン機能進捗メモ (2026-04-07)

## 現在地

- 目標: WhiteBrowser由来スキン機能をWebView2で表示可能にし、検索・サムネイル更新契約まで実運用可能にする。
- 進捗評価: **Phase1/2の中核は成立**、旧WB互換の高度APIまでは次段階。

## 実装済み主要事項

- `skin` 資産とソースを分離した。
  - 実行資産: `skin\` (`Compat`, `DefaultGridWB`, `SimpleGridWB`)
  - スキン実装ソース: `WhiteBrowserSkin\`
- `MainWindow` 側のWebView2外部スキン初期化を安定化。
  - `Views/Main/MainWindow.WebViewSkin.cs`
  - `Views/Main/MainWindow.xaml.cs`
  - `WhiteBrowserSkin/MainWindow.Skin.cs`
- `__external` 配信パスとサムネイル配信を実機で確認。
  - `WhiteBrowserSkin/Runtime/WhiteBrowserSkinRuntimeBridge.cs`
  - `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinRuntimeBridgeIntegrationTests.cs`
- サムネイル契約（`dbIdentity`, `recordKey`, `thumbRevision`, `?rev=`）を固定。
  - `WhiteBrowserSkin/Runtime/WhiteBrowserSkinDbIdentity.cs`
  - `WhiteBrowserSkin/Runtime/WhiteBrowserSkinThumbnailContractService.cs`
- サムネ import/更新経路の同期を修正し、実インポート時の反映を担保。
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateResultFinalizer.cs`
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
- 検索 bridge を最小実装し、完了待ちを確実化。
  - `WhiteBrowserSkin/Runtime/WhiteBrowserSkinApiService.cs`
  - `Views/Main/MainWindow.Search.cs`
  - `Views/Main/MainWindow.WebViewSkin.Api.cs`
- 外部skin起動確認用サンプルを追加し、UTF-8 文字化け対策を実施。
  - `skin/SimpleGridWB/SimpleGridWB.htm`
  - `skin/SimpleGridWB/SimpleGridWB.css`
  - `skin/DefaultGridWB/DefaultGridWB.htm`
- 最小ヘッダー（Host Chrome Minimal）を導入して外部スキン表示時のUI混在を整理。
  - `Views/Main/MainWindow.xaml`
  - `Views/Main/MainWindow.WebViewSkin.Chrome.cs`
- UI統合テストを拡充。
  - `Tests/IndigoMovieManager.Tests/MainWindowWebViewSkinIntegrationTests.cs`
  - `Tests/IndigoMovieManager.Tests/ExternalSkinHostRefreshSchedulerTests.cs`
  - `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinRuntimeBridgeIntegrationTests.cs`
  - `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinApiServiceTests.cs`
  - `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinThumbnailContractServiceTests.cs`

## 主要コミット（到達点）

- `ed201b6` WhiteBrowserスキンPhase1基盤を追加
- `54af3f9` WhiteBrowserSkinへsourceを分離しskinをasset専用化
- `490a331` 外部スキン向けHost Chrome Minimalを追加
- `af9a640` 外部スキンrefresh直列化をschedulerへ分離
- `405f09a` WB RuntimeBridge統合テストと実素材fixtureを追加
- `f5f5455` サムネimport marker同期とWB契約テストを補強
- `873b813` 外部スキン検索bridgeの完了待ちを保証
- `1c4ef0c` 外部skin動作確認用SimpleGridWBを追加
- `7e3c928` sample skinの文字コード宣言をutf-8へ修正
- `25b8829` 起動時外部skin hostの初期化を安定化
- `a5fa9bb` 外部スキン休止時の引き継ぎメモを追加
- `2d2c02e` WebView2実機起動の知見メモを追加

## 実機観点の到達ノート

- `skin-webview` ログで起動時に `active=True ready=True` の遷移確認。
- `system.skin=SimpleGridWB` 時の再起動/適用経路を再現できる状態。
- `Shift_JIS` 固定宣言による文字化けは修正済み（UTF-8 明示へ統一）。

## 未完（次に着手する候補）

1. 本格互換APIの拡張（優先: `wb.sort`, `onUpdate` / `onUpdateThum`, `onCreateThum`）
2. スキン起動時の最小以外の表示モード（既存5x2など）を内蔵スキン化し、切替導線を堅くする
3. 実行時の診断ログを追加し、200/403/404 とキャッシュ更新の差分を運用的に監視できる形へ

## 参考ドキュメント

- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `WhiteBrowserSkin/Docs/提案書_WebView2スキン完全互換向け_サムネ表示層と生成層の境界_2026-04-01.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
- `WhiteBrowserSkin/Docs/PM_Handoff_外部スキン一時休止_2026-04-02.md`
