# Execution Board: WebView2スキン完全互換 Phase 1 2026-04-01

最終更新日: 2026-04-01

## 1. 目的
- Codex は PM として進行管理に徹する
- 実装は複数の GPT-5.4 実装係へ分担する
- Phase 1 の完了条件を崩さず、並列作業で前へ進める

## 2. スコープ
- 対象は `Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md` の Phase 1
- Phase 0 の 5 決定は固定済みとして扱う
- Phase 2 以降の `wb.*` 本実装は今回の主対象外

## 3. 役割分担

### 3.1 実装係A: WebView2基盤
- 所有ファイル
  - `IndigoMovieManager.csproj`
  - `WhiteBrowserSkin/Host/**`
  - `WhiteBrowserSkin/Runtime/**`
  - `skin/Compat/**`
- 成果物
  - WebView2 host UserControl
  - encoding normalizer
  - runtime bridge / render coordinator の骨組み
  - `wblib-compat.js` のスケルトン

### 3.2 実装係B: スキン管理と設定連携
- 所有ファイル
  - `WhiteBrowserSkin/WhiteBrowserSkinDefinition.cs`
  - `WhiteBrowserSkin/WhiteBrowserSkinConfig.cs`
  - `WhiteBrowserSkin/WhiteBrowserSkinCatalogService.cs`
  - `WhiteBrowserSkin/MainWindow.Skin.cs` または `WhiteBrowserSkin/WhiteBrowserSkinOrchestrator.cs`
  - `Views/Settings/CommonSettingsWindow.xaml`
  - `Views/Settings/CommonSettingsWindow.xaml.cs`
  - `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinCatalogServiceTests.cs`
- 成果物
  - skin 管理の Orchestrator 化
  - 設定画面との安定連携
  - MainWindow 受け側へ渡す API 面の整理

### 3.3 実装係C: MainWindow 統合
- 所有ファイル
  - `Views/Main/MainWindow.xaml`
  - `Views/Main/MainWindow.xaml.cs`
  - `Views/Main/MainWindow.*.cs` の新規 partial
- 成果物
  - 外部スキン時だけ host を差し込める統合口
  - DB切替 / skin切替 / タブ切替の呼び出し点整理
  - WPF 側が正本である前提を壊さないイベント経路

### 3.4 レビュー係
- 役割
  - P1/P2 レベルの重大論点抽出
  - Phase 1 完了条件の追加観点指摘
  - テスト不足の洗い出し

### 3.5 git係
- 役割
  - dirty worktree を前提に安全なステージ範囲を切る
  - 混ぜてはいけない差分を明示する
  - 最終段階の commit / push 手順を non-interactive で整える

## 4. 依存関係

### 4.1 A -> C
- C は A が用意する host / runtime の public 面に接続する

### 4.2 B -> C
- C は B が定義する Orchestrator / skin 管理 API に接続する

### 4.3 A <-> B
- A は host / runtime に必要な skin DTO 定義だけ共有する
- B は WebView2 実装詳細へ踏み込まない

## 5. 受け渡し条件

### 5.1 実装係A
- WebView2 host が単独で初期化できる
- Shift_JIS -> UTF-8 正規化の入口がある
- `skin.local` / `thum.local` を扱う足場がある

### 5.2 実装係B
- MainWindow から呼ぶ skin 管理 API が明確
- `system.skin` / `profile` の責務が整理されている
- 設定画面から skin 切替できる

### 5.3 実装係C
- 外部 skin 時だけ host を出せる
- 既存 Default5 タブの高速経路を壊していない
- DB / skin / tab の切替点が partial で整理されている

## 6. 完了条件
- Phase 1 の完了条件を満たす
- Phase 0 の 5 決定を覆す設計変更が入っていない
- dirty worktree の unrelated 変更を巻き込んでいない
- build と必要最小限テストが通る

## 7. PM の判断基準
- テンポを落とさないか
- MainWindow へ責務を戻していないか
- 選択状態の正本がぶれていないか
- `file:///` へ逃げていないか
- Shift_JIS 問題を後回しにしていないか
