# PM 実行ボード: WebView2 による WhiteBrowser スキン完全互換 2026-04-01

最終更新日: 2026-04-01

## 1. 目的
- Codex 本体は PM として進行管理・設計判断・統合判断だけを行う
- 実装は GPT-5.4 サブエージェントへ分担する
- Git 担当とレビュー担当を分け、実装と統合の衝突を減らす

## 2. スコープ
- 直近の完了目標は `Phase 1: WebView2 ホスト導入` の成立
- ただし、`Phase 2` へ繋がる骨組みまでは一緒に作る
- `Phase 0` の 5 決定は固定済み前提で進める

## 3. 役割分担

### 3.1 実装係A: WebView2 基盤
所有:
- `IndigoMovieManager.csproj`
- `WhiteBrowserSkin/Host/**`
- `WhiteBrowserSkin/Runtime/**`
- `skin/Compat/**`

責務:
- WebView2 パッケージ導入
- Host UserControl
- Runtime bridge / render coordinator / encoding normalizer の骨組み
- `skin.local` / `thum.local` を前提にした基盤
- `wblib-compat.js` の土台

### 3.2 実装係B: スキン管理 / Orchestrator / 設定導線
所有:
- `WhiteBrowserSkin/WhiteBrowserSkinDefinition.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinConfig.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinCatalogService.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs`
- `WhiteBrowserSkin/MainWindow.Skin.cs`
- `Views/Settings/CommonSettingsWindow.xaml`
- `Views/Settings/CommonSettingsWindow.xaml.cs`

責務:
- `MainWindow.Skin.cs` 直結責務の整理
- Orchestrator 導入
- 設定画面からの切替導線維持
- Runtime 未導入時のフォールバック方針の受け口

### 3.3 実装係C: MainWindow 統合
所有:
- `Views/Main/MainWindow.xaml`
- `Views/Main/MainWindow.xaml.cs`
- 必要なら `Views/Main/MainWindow.WebViewSkin.cs` 新設

責務:
- WPF 標準タブと WebView2 ホストの二刀流切替
- 外部スキン時の表示 host 差し込み
- DB 切替 / スキン切替 / 右クリック / ショートカットとの接点整理

### 3.4 レビュー担当
所有:
- 読み取り専任

責務:
- 設計逸脱の検出
- `Phase 0` の 5 決定が破られていないか確認
- `MainWindow` へ責務を戻していないか確認
- 既存 WPF 高速タブを壊していないか確認

### 3.5 Git 担当
所有:
- Git 操作のみ

責務:
- 現在の dirty worktree を前提に、安全な staging 範囲を決める
- 実装完了後に対象ファイルだけを抽出する
- `git diff --check` / `git status --short` の確認
- 必要ならブランチ整理、コミット、push、PR まで担当

## 4. 依存関係

### 4.1 先行
- 実装係A が先に WebView2 host / runtime 骨組みを作る
- 実装係B が Orchestrator 入口を固める

### 4.2 後追い
- 実装係C は A/B の骨組みが見えた段階で `MainWindow` 側へ接続する

### 4.3 レビューゲート
- A/B の一次レビュー
- C 統合後の全体レビュー
- Git 担当の最終差分確認

## 5. 品質ゲート
- `Phase 0` の 5 決定をひっくり返す変更は禁止
- `file:///` 直参照は禁止
- 選択状態の正本を WebView 側へ寄せる変更は禁止
- WebView2 無しで既存 WPF タブを壊す変更は禁止
- `MainWindow` に新しい巨大責務を戻す変更は禁止

## 6. 直近タスク順
1. 実装係A が WebView2 基盤を作る
2. 実装係B が Orchestrator と設定導線を整理する
3. レビュー担当が A/B を先行レビューする
4. 実装係C が MainWindow へ統合する
5. レビュー担当が統合レビューする
6. Git 担当が staging 範囲を確定する

## 7. PM の責務
- サブエージェントの担当衝突を避ける
- 設計判断の最終決定を行う
- レビュー指摘を優先順位付きで返す
- 最終的にユーザーへ進捗と判断理由を報告する
