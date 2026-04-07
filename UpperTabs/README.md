# UpperTabs ドキュメント案内

このフォルダは、上側タブ (`Small / Big / Grid / List / Big10 / 救済 / 重複動画`) の設計と実装資料の置き場です。

## 入口

- [設計メモ_上側タブ共通描画再検討_2026-03-15.md](Docs/設計メモ_上側タブ共通描画再検討_2026-03-15.md)
  - 上側タブの高速化を前提に、共通描画クラスではなく共通描画基盤を採る判断を整理した設計メモです。
- [Implementation Plan_上側タブvisible-first高速化_2026-03-15.md](Docs/Implementation Plan_上側タブvisible-first高速化_2026-03-15.md)
  - 上側タブの visible-first 高速化を、decode 最適化から queue priority まで段階導入する実装計画です。
- [Review_上側タブvisible-first高速化_2026-03-15.md](Docs/Review_上側タブvisible-first高速化_2026-03-15.md)
  - 上側タブの visible-first 高速化計画に対するレビューと、着手前に固めるべき論点です。
- [DuplicateVideos/Implementation Plan_重複動画タブ_2026-03-20.md](DuplicateVideos/Implementation Plan_重複動画タブ_2026-03-20.md)
  - 上側に `重複動画` タブを新設し、同一ハッシュ検出を手動分析タブとして導入する計画です。
- [../Docs/forAI/比較メモ_UpperTabsCommon_BottomTabsCommon_役割対応_2026-04-01.md](../Docs/forAI/比較メモ_UpperTabsCommon_BottomTabsCommon_役割対応_2026-04-01.md)
  - `UpperTabs/Common` と `BottomTabs/Common` の責務差を横断で確認する比較メモです。
- [../Docs/forAI/比較メモ_UpperTabsCommon_BottomTabsCommon_ここに置かない物_2026-04-01.md](../Docs/forAI/比較メモ_UpperTabsCommon_BottomTabsCommon_ここに置かない物_2026-04-01.md)
  - `Common` に置かない物の禁止線を確認する比較メモです。

## 方針

- 上側タブ専用の文書は、このフォルダへ寄せる。
- コードを追加したら、この `README.md` も同時に更新する。
- `MainWindow.xaml` 直下に置かれていた設計メモは、今後このフォルダを基準に辿る。
- タブ専用の実装計画は、対象別サブフォルダを切って置く。

## 実装メモ

- 上側タブの共通基盤コードは `UpperTabs/Common` に置く。
- `UpperTabs/Common` には、主に次を置く。
  - identity 系: 現在タブ判定、通常タブ判定、固定IDと `TabItem`・host の相互解決、skin 互換名の往復解決
  - viewport 系: 更新 context 解決、visible range 解決、差分解決、snapshot 反映、不可時処理、ログ、自動救済分岐
  - selection 系: selection change context 解決、通常/特殊タブの振り分け、既定選択、選択解決、選択反映、詳細ペイン同期、後半処理
  - 運用ルール: `TryResolve... / Dispatch... / Finalize...` の読み味を揃える
- `Viewport.cs` の helper は、更新フロー順に上から追える並びを保つ。
- `SelectionFlow.cs` の helper も、選択変更フロー順に上から追える並びを保つ。
- `UpperTabs/Small` `UpperTabs/Big` `UpperTabs/Grid` `UpperTabs/List` `UpperTabs/Big10` は、上側通常タブの個別 dir 化を始める入口と選択取得 helper を置く。
- `UpperTabs/Rescue` は、上側 `救済` タブ専用の UI、軽量一覧モデル、既定選択 helper を置く。
- `UpperTabs/DuplicateVideos` は、上側 `重複動画` タブの UI、検出、表示モデル、既定選択 helper を置く。2026-03-20 時点で左右2ペインの初期版を実装済み。
- 2026-03-15 時点で、Phase 1 の decode 最適化、Phase 2 の viewport 追跡、Phase 3 の visible-first lease 優先制御まで着手済み。
