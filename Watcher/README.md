# Watcher ドキュメント案内

このフォルダは、監視、FileIndex、Everything連携、MainDB登録まわりの文書を扱います。
設計資料と不具合対応が混在しているため、読む順番を明示します。

## 人間向けの入口

- [Everything_to_Everything_Flow_Design_2026-02-28.md](Everything_to_Everything_Flow_Design_2026-02-28.md)
  - 現行の差分検証アーキテクチャの中心資料です。
- [Everything_to_DB_Registration_Flow_2026-02-28.md](Everything_to_DB_Registration_Flow_2026-02-28.md)
  - 監視からDB登録までの流れを追えます。
- [DB_Switch_Safety_Design_2026-03-01.md](DB_Switch_Safety_Design_2026-03-01.md)
  - DB切り替え時の安全対策です。

## 現状のコード配置 (2026-03-12)

- `Watcher` フォルダには文書と現役コードが混在します。
  - `EverythingProvider.cs`、`IndexProviderFacade.cs`、`MainWindow.Watcher.cs` などが現行の入口です。
- `WatchWindow.xaml(.cs)` は監視フォルダ編集画面で、2026-03-20 時点ではフォームへのフォルダドロップ登録にも対応します。
- `MainWindow.Watcher.cs` は 2026-03-20 時点で、watch suppression の入口制御、deferred state / last-sync cursor の stale guard、catch-up / 遅延 reload の本体を持ちます。
- `MainWindow.WatchScanCoordinator.cs` は per-file / per-batch の調停役です。watch 起点の UI append / queue flush は直前で suppression を再確認しますが、deferred state の保存や catch-up 自体は `MainWindow.Watcher.cs` 側にあります。
- FileIndex の実装本体は `src/IndigoMovieManager.FileIndex.UsnMft` にあります。
- `EverythingLite` は既定で内包版を使い、必要時のみ外部参照へ切り替える構成です。

## AI / 実装向けの入口

- [Implementation Plan.md](Implementation Plan.md)
  - Watcher系の基本計画です。

## 直近の作業入口

- [EverythingLite内包境界定義_2026-03-05.md](EverythingLite内包境界定義_2026-03-05.md)
- [Implementation Plan_EverythingLite内包取り込み_分離可能化_実装計画兼タスクリスト_2026-03-05.md](Implementation Plan_EverythingLite内包取り込み_分離可能化_実装計画兼タスクリスト_2026-03-05.md)
- [Implementation Plan_Everything専用監視モード導入_2026-03-05.md](Implementation Plan_Everything専用監視モード導入_2026-03-05.md)
- [Flowchart_メインDB登録非同期化現状_2026-03-05.md](Flowchart_メインDB登録非同期化現状_2026-03-05.md)
- [調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md](調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md)
- [AI向け_引き継ぎ_Watcher責務分離_UI詰まり防止_2026-03-20.md](AI向け_引き継ぎ_Watcher責務分離_UI詰まり防止_2026-03-20.md)

## 設計・責務整理

- [Everything_DLL_API案_2026-03-03.md](Everything_DLL_API案_2026-03-03.md)
- [Everything_MainWindow置換ポイント一覧_2026-03-03.md](Everything_MainWindow置換ポイント一覧_2026-03-03.md)
- [Everything_Phase2_移植単位一覧_2026-03-03.md](Everything_Phase2_移植単位一覧_2026-03-03.md)
- [Everything_reason_code契約_2026-03-03.md](Everything_reason_code契約_2026-03-03.md)
- [Everything_フォールバック条件表_2026-03-03.md](Everything_フォールバック条件表_2026-03-03.md)
- [UI改修ガイド_MainDB書き込み前仮表示仕様_2026-03-03.md](UI改修ガイド_MainDB書き込み前仮表示仕様_2026-03-03.md)

## 実装計画

- [Implementation Plan_Everything高速後_MainDB書き込み詰まり解消_2026-03-01.md](Implementation Plan_Everything高速後_MainDB書き込み詰まり解消_2026-03-01.md)
- [Implementation Plan_Everything連携DLL分離_棚卸し含有範囲決定_2026-03-03.md](Implementation Plan_Everything連携DLL分離_棚卸し含有範囲決定_2026-03-03.md)
- [Implementation Plan_Everything連携DLL分離_Phase1詳細_2026-03-03.md](Implementation Plan_Everything連携DLL分離_Phase1詳細_2026-03-03.md)
- [Implementation Plan_Everything連携DLL分離_Phase2詳細_2026-03-03.md](Implementation Plan_Everything連携DLL分離_Phase2詳細_2026-03-03.md)
- [Implementation Plan_FileIndexProvider_UI切替_AB差分テスト_2026-03-03.md](Implementation Plan_FileIndexProvider_UI切替_AB差分テスト_2026-03-03.md)
- [Implementation Plan_サムネイルjpg削除時再作成漏れ対策_実装計画兼タスクリスト_2026-03-04.md](Implementation Plan_サムネイルjpg削除時再作成漏れ対策_実装計画兼タスクリスト_2026-03-04.md)

## 障害対応・補足資料

- [DB_Switch_Safety_Design_2026-03-01.md](DB_Switch_Safety_Design_2026-03-01.md)
- [DB_Switch_Safety_Design_2026-03-01_ドラフト.md](DB_Switch_Safety_Design_2026-03-01_ドラフト.md)
- [タブ別サムネ未生成バグ_完全決着編_2026-03-01.md](タブ別サムネ未生成バグ_完全決着編_2026-03-01.md)
- [Gemini向け_タブ別サムネ未生成バグ_原因対策_2026-03-01.md](Gemini向け_タブ別サムネ未生成バグ_原因対策_2026-03-01.md)
- [Architecture_Evaluation_Everything_Diff_2026-02-28.md](Architecture_Evaluation_Everything_Diff_2026-02-28.md)

## 配置ルール

- Watcher / FileIndex / Everything に閉じる文書は `Watcher` に置く
- UIとの境界を扱う文書でも、主体が監視系ならここに置く
- 文書追加時は、この案内から辿れる状態を維持する
- `Watcher` と `src/IndigoMovieManager.FileIndex.UsnMft` の関係が変わった時は、この案内を先に直す
