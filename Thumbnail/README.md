# Thumbnail ドキュメント案内

このフォルダは、サムネイル生成まわりの設計、実装計画、調査、運用資料をまとめて扱います。
資料数が多いため、目的別に入口を分けています。

- **Docs/** — 現行アクティブな資料（26本）
- **Docs/history/** — 完了済み・参考用の歴史的資料（29本）
- **救済worker/** — 救済exe関連の専用資料
- **Test/** — テスト計画・回帰チェック

## 人間向けの入口

- [../Docs/ThumbnailLogic_2026-02-28.md](../Docs/ThumbnailLogic_2026-02-28.md)
  - サムネイル全体の基本設計です。
- [Docs/現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md](Docs/現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md)
  - `workthree` 観点の現状整理です。
- [Docs/手動再試行運用手順.md](Docs/手動再試行運用手順.md)
  - 運用時の再試行手順です。
- [Docs/救済レーン実動画確認チェックリスト_2026-03-12.md](Docs/救済レーン実動画確認チェックリスト_2026-03-12.md)
  - 実動画確認のチェック観点です。

## 調査補助ツール

- SQLite CLI:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\tools\sqlite-tools\sqlite3.exe`
  - `FailureDb` の `pending_rescue / processing_rescue / rescued / gave_up` 件数確認や、`attempt_failed` 行の追跡に使う。
  - 例:
    - `& "C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\tools\sqlite-tools\sqlite3.exe" "%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\FailureDb\難読.9A45F494.failure.imm" "select FailureId, Status, Engine, FailureReason from ThumbnailFailure order by FailureId desc limit 20;"`

## 現状のコード配置 (2026-03-12)

- `Thumbnail` フォルダは文書置き場だけではありません。
  - `MainWindow.ThumbnailCreation.cs` など、本体アプリ側のコードがあります。
- エンジン系の実コードは `src/IndigoMovieManager.Thumbnail.Engine` から参照されます。
  - 一部は `Thumbnail` 配下のファイルをリンク参照して再利用しています。
- キュー系の本体は `src/IndigoMovieManager.Thumbnail.Queue` にあります。
- テスト資料は `Test/README.md` を入口に追うと迷いにくいです。

## AI / 実装向けの入口

- [Docs/history/Implementation Plan.md](Docs/history/Implementation%20Plan.md)
  - この領域の基礎計画です。
- [Test/README.md](Test/README.md)
  - テスト計画と回帰観点の入口です。

## 直近の作業入口

- [Docs/Implementation Plan_サムネイル救済処理_ERROR動画一括救済_2026-03-12.md](Docs/Implementation%20Plan_サムネイル救済処理_ERROR動画一括救済_2026-03-12.md)
- [Docs/救済レーン実動画確認チェックリスト_2026-03-12.md](Docs/救済レーン実動画確認チェックリスト_2026-03-12.md)
- [Docs/優先順位表_workthree_失敗9件の検証順_2026-03-11.md](Docs/優先順位表_workthree_失敗9件の検証順_2026-03-11.md)
- [Docs/調査結果_サムネエンジン比較_fork大粒度アーキ_リペア処理_並列管理の移植観点_2026-03-11.md](Docs/調査結果_サムネエンジン比較_fork大粒度アーキ_リペア処理_並列管理の移植観点_2026-03-11.md)

## 救済worker 資料

- [救済worker/伝達書_救済worker_Debug実行切り分け_2026-03-15.md](救済worker/伝達書_救済worker_Debug実行切り分け_2026-03-15.md)
- [救済worker/中期計画_救済exe段階改善_2026-03-15.md](救済worker/中期計画_救済exe段階改善_2026-03-15.md)
- [救済worker/設計メモ_救済exe処理順とFailureDb書込アルゴ再考_2026-03-15.md](救済worker/設計メモ_救済exe処理順とFailureDb書込アルゴ再考_2026-03-15.md)
- [救済worker/未解決束レポート_p6_2026-03-15.md](救済worker/未解決束レポート_p6_2026-03-15.md)

## 設計とアーキテクチャ

- [Docs/history/plan_AsyncQueueDbArchitecture_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md](Docs/history/plan_AsyncQueueDbArchitecture_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md)
- [Docs/DCO_エンジン分離実装規則_2026-03-05.md](Docs/DCO_エンジン分離実装規則_2026-03-05.md)
- [Docs/DEC_サムネイル並列レーン閾値プリセット方針_2026-03-05.md](Docs/DEC_サムネイル並列レーン閾値プリセット方針_2026-03-05.md)
- [Docs/Flowchart_動画情報取得_サムネイル作成_ハッシュ作成タイミング_2026-03-04.md](Docs/Flowchart_動画情報取得_サムネイル作成_ハッシュ作成タイミング_2026-03-04.md)
- [Docs/history/動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md](Docs/history/動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md)

## 実装計画（完了済み — history）

- [Docs/history/Implementation Plan_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md](Docs/history/Implementation%20Plan_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md)
- [Docs/history/Implementation Plan_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md](Docs/history/Implementation%20Plan_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md)
- [Docs/history/Implementation Plan_Phase3_キュー進捗通知インターフェース化_2026-03-04.md](Docs/history/Implementation%20Plan_Phase3_キュー進捗通知インターフェース化_2026-03-04.md)
- [Docs/history/Implementation Plan_Phase4_Rust外出し準備_QueueDBハッシュ保持_マイグレーションなし_2026-03-04.md](Docs/history/Implementation%20Plan_Phase4_Rust外出し準備_QueueDBハッシュ保持_マイグレーションなし_2026-03-04.md)
- [Docs/history/Implementation Plan_サムネイル並列レーン化と大動画低優先制御_実装計画兼タスクリスト_2026-03-05.md](Docs/history/Implementation%20Plan_サムネイル並列レーン化と大動画低優先制御_実装計画兼タスクリスト_2026-03-05.md)

## 調査・ベンチ・障害対応

- [Docs/history/Hash取得ベンチ結果_2026-02-25.md](Docs/history/Hash取得ベンチ結果_2026-02-25.md)
- [Docs/history/ライブラリ比較_変換速度ベンチ結果_2026-02-25.md](Docs/history/ライブラリ比較_変換速度ベンチ結果_2026-02-25.md)
- [Docs/history/MovieInfo_FFMediaToolkit切替影響範囲とベンチ_2026-02-25.md](Docs/history/MovieInfo_FFMediaToolkit切替影響範囲とベンチ_2026-02-25.md)
- [Docs/history/EmojiPathStatus_2026-03-01.md](Docs/history/EmojiPathStatus_2026-03-01.md)
- [Docs/サムネイルが作成できない動画対策.md](Docs/サムネイルが作成できない動画対策.md)

## テスト・運用

- [Test/README.md](Test/README.md)
  - テスト戦略、修正リスト、ベンチまとめの入口です。
- [Docs/history/ManualRegressionCheck_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md](Docs/history/ManualRegressionCheck_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md)
- [Docs/history/ManualRegressionCheck_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md](Docs/history/ManualRegressionCheck_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md)
- [Docs/手動再試行運用手順.md](Docs/手動再試行運用手順.md)

## 配置ルール

- サムネイル機能に閉じる文書は `Thumbnail` に置く
  - アクティブな資料は `Docs/` に、完了済みは `Docs/history/` に配置する
- コード横に置くことで、実装と一緒に更新しやすくする
- 新しい計画書や調査書を増やしたら、この案内も更新する
- `src` 配下へ分離済みの実装があるため、文書更新時は `Thumbnail` と `src` の両方を意識する
