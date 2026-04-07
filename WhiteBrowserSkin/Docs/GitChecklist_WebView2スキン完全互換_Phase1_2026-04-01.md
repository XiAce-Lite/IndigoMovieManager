# Git チェックリスト: WebView2 スキン完全互換 Phase 1 2026-04-01

## 1. staging 前
- `git status --short` で既存の unrelated 変更を把握する
- 今回の対象ファイルだけを明示する
- `skin/` と WebView2 関連に限定して混入がないか確認する

## 2. staging 対象
- `IndigoMovieManager.csproj`
- `WhiteBrowserSkin/Host/**`
- `WhiteBrowserSkin/Runtime/**`
- `skin/Compat/**`
- `WhiteBrowserSkin/WhiteBrowserSkin*.cs`
- `WhiteBrowserSkin/MainWindow.Skin.cs`
- `Views/Settings/CommonSettingsWindow.*`
- `Views/Main/MainWindow.*` のうち WebView2 接続に必要な差分
- 関連テスト

## 3. staging 後
- `git diff --cached --stat`
- `git diff --check --cached`
- ローカル固有情報が無いか確認

## 4. コミット方針
- 1コミット1目的を守る
- 推奨分割
  1. WebView2 基盤
  2. Orchestrator / 設定導線
  3. MainWindow 統合
  4. テスト / ドキュメント

## 5. push / PR 前
- Runtime 未導入時の挙動説明があるか
- `Phase 0` の 5 決定が PR 説明に書かれているか
- 既存標準タブを壊していない確認結果があるか
