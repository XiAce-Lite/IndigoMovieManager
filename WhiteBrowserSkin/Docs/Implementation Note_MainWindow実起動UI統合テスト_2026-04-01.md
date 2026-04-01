# MainWindow実起動UI統合テスト 実装メモ 2026-04-01

## 目的

- `MainWindow` を実際に起動した状態で、WebView2 外部 skin と既存 WPF タブの見た目切替を確認できるようにする。
- `ExternalSkinHostRefreshScheduler` の世代競合が、最終表示へ正しく反映されることを MainWindow 実 UI で固定する。

## 今回入れたもの

- `Views/Main/MainWindow.WebViewSkin.cs`
  - `ExternalSkinHostPrepareAsyncForTesting`
  - `ExternalSkinHostPresentationAppliedForTesting`
  - host ready / fallback の待ち合わせを実 UI テスト側から観測できる最小フック
- `Views/Main/MainWindow.xaml.cs`
  - `SkipMainWindowClosingSideEffectsForTesting`
  - 実 UI テスト時に static queue 完了や settings 保存を避けるための最小ガード
- `Tests/IndigoMovieManager.Tests/MainWindowWebViewSkinIntegrationTests.cs`
  - shared STA UI thread + `Dispatcher.Run()` + hidden `MainWindow.Show()` で 4 ケースを追加

## 固定した確認項目

1. 外部 skin 有効かつ host ready なら `Tabs=Collapsed`、`ExternalSkinHostPresenter=Visible`、`Content` に host control が入る
2. html 欠落または host 準備失敗なら WPF fallback に戻る
3. skin 切替競合で古い refresh が遅れて完了しても、最終表示は最新 generation のまま巻き戻らない

## テスト上の扱い

- 本物の `MainWindow` を hidden 表示で起動する
- `AutoOpen=false`、`ConfirmExit=false`、`ThemeMode=Original` をテスト中だけ適用する
- `layout.xml` 汚染を避けるため、テストごとに `Environment.CurrentDirectory` を一時ディレクトリへ切り替える
- `Close()` で実際の終了経路を通す
- ただしテスト用ガードで、設定保存やプロセス全体停止だけを抑止し、UI タイマー停止などの window 局所 cleanup は残す

## メモ

- `WhiteBrowserSkinRuntimeBridgeIntegrationTests` が実 WebView2 応答を持っているため、
  `MainWindowWebViewSkinIntegrationTests` では表示切替境界の確認へ責務を絞っている
- WebView2 runtime の有無そのものは、ここでは再検証しない
