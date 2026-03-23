# ドキュメント案内

このファイルは、リポジトリ内に散在している文書の入口です。
既存資料はなるべく動かさず、参照先を見つけやすくすることを目的にしています。

## 人間向けの入口

- [ProjectOverview_2026-02-28.md](ProjectOverview_2026-02-28.md)
  - プロジェクト全体の入口です。
- [DevelopmentSetup_2026-02-28.md](DevelopmentSetup_2026-02-28.md)
  - 開発環境と基本手順を確認できます。
- [Architecture_2026-02-28.md](Architecture_2026-02-28.md)
  - 主要な責務分割を把握できます。
- [DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)
  - DBまわりの前提を確認できます。
- [人間向け_大粒度フロー_DBとプロジェクト_現状と完成形_2026-03-20.md](人間向け_%E5%A4%A7%E7%B2%92%E5%BA%A6%E3%83%95%E3%83%AD%E3%83%BC_DB%E3%81%A8%E3%83%97%E3%83%AD%E3%82%B8%E3%82%A7%E3%82%AF%E3%83%88_%E7%8F%BE%E7%8A%B6%E3%81%A8%E5%AE%8C%E6%88%90%E5%BD%A2_2026-03-20.md)
  - DB とプロジェクトだけで全体像を掴むための大粒度資料です。
- [ThumbnailLogic_2026-02-28.md](ThumbnailLogic_2026-02-28.md)
  - サムネイル系の全体像です。
- [RegressionChecklist.md](RegressionChecklist.md)
  - 手動確認の入口です。

## 現状の構成 (2026-03-12)

- 本体は `IndigoMovieManager_fork.csproj`
  - `net8.0-windows` / WPF / `x64` 固定です。
- サブプロジェクトは `src` 配下の 3 本です。
  - `IndigoMovieManager.Thumbnail.Engine`
  - `IndigoMovieManager.Thumbnail.Queue`
  - `IndigoMovieManager.FileIndex.UsnMft`
- テストは `Tests/IndigoMovieManager_fork.Tests`
  - NUnit ベースです。
- `Thumbnail` と `Watcher` にはコードと文書が混在します。
  - 文書だけでなく、現役コードもある前提で辿ってください。

## AI / 実装向けの入口

- [../AI向け_現在の全体プラン_workthree_2026-03-20.md](../AI向け_%E7%8F%BE%E5%9C%A8%E3%81%AE%E5%85%A8%E4%BD%93%E3%83%97%E3%83%A9%E3%83%B3_workthree_2026-03-20.md)
  - `workthree` の現在の大粒度優先順位と着手順です。AI はまずここを見ます。
- [Implementation Plan_完成形移行_超大粒度ロードマップ_2026-03-20.md](Implementation%20Plan_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C_%E8%B6%85%E5%A4%A7%E7%B2%92%E5%BA%A6%E3%83%AD%E3%83%BC%E3%83%89%E3%83%9E%E3%83%83%E3%83%97_2026-03-20.md)
  - 完成形へ向かう責務移行を、AI 分担レーン単位で俯瞰する超大粒度計画です。
- [AI向け_運用ボード_完成形移行_2026-03-20.md](AI%E5%90%91%E3%81%91_%E9%81%8B%E7%94%A8%E3%83%9C%E3%83%BC%E3%83%89_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C_2026-03-20.md)
  - 調整役、実装役、レビュー専任役へタスクを割り振る運用ボードです。
- [AI向け_分担計画_完成形移行_次フェーズ_2026-03-20.md](AI%E5%90%91%E3%81%91_%E5%88%86%E6%8B%85%E8%A8%88%E7%94%BB_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C_%E6%AC%A1%E3%83%95%E3%82%A7%E3%83%BC%E3%82%BA_2026-03-20.md)
  - 次フェーズを `ThumbnailError` / `Watcher` / `UI hang` の 3 レーンへ分ける配布用計画です。
- [AI向け_配布文面_完成形移行次フェーズ3タスク_2026-03-20.md](AI%E5%90%91%E3%81%91_%E9%85%8D%E5%B8%83%E6%96%87%E9%9D%A2_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E6%AC%A1%E3%83%95%E3%82%A7%E3%83%BC%E3%82%BA3%E3%82%BF%E3%82%B9%E3%82%AF_2026-03-20.md)
  - 実装役 A / B / C とレビュー専任役へそのまま渡せる次フェーズ配布文面です。
- [AI向け_レビュー結果_完成形移行次フェーズ1回目_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E6%AC%A1%E3%83%95%E3%82%A7%E3%83%BC%E3%82%BA1%E5%9B%9E%E7%9B%AE_2026-03-20.md)
  - `T8` / `T9` / `T10` の受け入れ結果と、`T9/C9` の本線 reconciliation 最終判定をまとめた記録です。
- [AI向け_コミット分割計画_完成形移行次フェーズ取り込み_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%82%B3%E3%83%9F%E3%83%83%E3%83%88%E5%88%86%E5%89%B2%E8%A8%88%E7%94%BB_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E6%AC%A1%E3%83%95%E3%82%A7%E3%83%BC%E3%82%BA%E5%8F%96%E3%82%8A%E8%BE%BC%E3%81%BF_2026-03-20.md)
  - 受け入れ済みの `T8` / `T9` / `T10` をどの帯でコミットへ分けるかの計画です。
- [AI向け_レビュー結果_Q1_ThumbnailQueueビルド不整合解消_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q1_ThumbnailQueue%E3%83%93%E3%83%AB%E3%83%89%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-20.md)
  - `Q1` の最終レビュー結果と、`CS1739` 解消の受け入れ記録です。
- [AI向け_レビュー結果_UIHang_dispatcher縮退帯_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_UIHang_dispatcher%E7%B8%AE%E9%80%80%E5%B8%AF_2026-03-22.md)
  - `UI hang` follow-up として、`dispatcher timer` fault 縮退帯の受け入れ、本線取り込み、residual dirty 回帰判定をまとめた記録です。
- [AI向け_レビュー結果_T10b_UIHang_followup_startup_manualplayer_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_T10b_UIHang_followup_startup_manualplayer_2026-03-22.md)
  - `UI hang` residual から再抽出した `startup activity` と `manual player resize` 帯の受け入れ結果です。
- [AI向け_作業指示_Codex_T10b_UIHang_followup_startup_manualplayer_2026-03-22.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_T10b_UIHang_followup_startup_manualplayer_2026-03-22.md)
  - `T10b` の実装役向けスコープと禁止線を固定した指示書です。
- [AI向け_レビュー指示_Claude_T10b_UIHang_followup_startup_manualplayer_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_T10b_UIHang_followup_startup_manualplayer_2026-03-22.md)
  - `T10b` 専用のレビュー観点です。
- [AI向け_レビュー結果_Watcher残差分帯分け_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Watcher%E6%AE%8B%E5%B7%AE%E5%88%86%E5%B8%AF%E5%88%86%E3%81%91_2026-03-22.md)
  - `Watcher` 残差分の混在レビュー結果と、次に切るべき最小レーンの判断記録です。
- [AI向け_作業指示_Codex_T9b_WatcherRenameBridge安全契約復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_T9b_WatcherRenameBridge%E5%AE%89%E5%85%A8%E5%A5%91%E7%B4%84%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `Watcher RenameBridge` の安全契約だけを戻す次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_T9b_WatcherRenameBridge安全契約復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_T9b_WatcherRenameBridge%E5%AE%89%E5%85%A8%E5%A5%91%E7%B4%84%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `T9b` 専用のレビュー観点です。
- [AI向け_レビュー結果_T9b_WatcherRenameBridge安全契約復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_T9b_WatcherRenameBridge%E5%AE%89%E5%85%A8%E5%A5%91%E7%B4%84%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `T9b` の受け入れ、clean commit、本線取り込みまでをまとめた記録です。
- [AI向け_作業指示_Codex_T9c_WatchFolderDrop正規化復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_T9c_WatchFolderDrop%E6%AD%A3%E8%A6%8F%E5%8C%96%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `WatchFolderDrop` の末尾セパレータ正規化と重複判定だけを戻す次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_T9c_WatchFolderDrop正規化復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_T9c_WatchFolderDrop%E6%AD%A3%E8%A6%8F%E5%8C%96%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `T9c` 専用のレビュー観点です。
- [AI向け_レビュー結果_T9c_WatchFolderDrop正規化復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_T9c_WatchFolderDrop%E6%AD%A3%E8%A6%8F%E5%8C%96%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `T9c` の受け入れ、clean commit、本線取り込みまでをまとめた記録です。
- [AI向け_レビュー結果_Watcher残差分再帯分け_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Watcher%E6%AE%8B%E5%B7%AE%E5%88%86%E5%86%8D%E5%B8%AF%E5%88%86%E3%81%91_2026-03-22.md)
  - `T9c` 後の `Watcher` 残差分を再帯分けし、次の最小レーンを `T9d` に固定した記録です。
- [AI向け_作業指示_Codex_T9d_WatcherCreatedSnapshot契約復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_T9d_WatcherCreatedSnapshot%E5%A5%91%E7%B4%84%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `Created` watch queue が DB / tab snapshot を保持する契約だけを戻す次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_T9d_WatcherCreatedSnapshot契約復元_2026-03-22.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_T9d_WatcherCreatedSnapshot%E5%A5%91%E7%B4%84%E5%BE%A9%E5%85%83_2026-03-22.md)
  - `T9d` 専用のレビュー観点です。
- [AI向け_レビュー結果_T9d_WatcherCreatedSnapshot契約復元_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_T9d_WatcherCreatedSnapshot%E5%A5%91%E7%B4%84%E5%BE%A9%E5%85%83_2026-03-23.md)
  - `T9d` は現行 snapshot で既に契約充足していたため、no-op 受け入れで閉じた記録です。
- [AI向け_作業指示_Codex_T9e_WatcherRenameBridge非表示item回帰修正_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_T9e_WatcherRenameBridge%E9%9D%9E%E8%A1%A8%E7%A4%BAitem%E5%9B%9E%E5%B8%B0%E4%BF%AE%E6%AD%A3_2026-03-23.md)
  - `RenameBridge` が visible item 依存へ戻った回帰だけを切る次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_T9e_WatcherRenameBridge非表示item回帰修正_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_T9e_WatcherRenameBridge%E9%9D%9E%E8%A1%A8%E7%A4%BAitem%E5%9B%9E%E5%B8%B0%E4%BF%AE%E6%AD%A3_2026-03-23.md)
  - `T9e` 専用のレビュー観点です。
- [AI向け_レビュー結果_T9e_WatcherRenameBridge非表示item回帰修正_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_T9e_WatcherRenameBridge%E9%9D%9E%E8%A1%A8%E7%A4%BAitem%E5%9B%9E%E5%B8%B0%E4%BF%AE%E6%AD%A3_2026-03-23.md)
  - `T9e` は現行 snapshot で既に契約充足していたため、no-op 受け入れで閉じた記録です。
- [AI向け_作業指示_Codex_T10d_DispatcherTimer起動縮退復元_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_T10d_DispatcherTimer%E8%B5%B7%E5%8B%95%E7%B8%AE%E9%80%80%E5%BE%A9%E5%85%83_2026-03-23.md)
  - `StartupUri` 前提の handler 登録順と `DispatcherTimer` start fault 伝播だけを戻す次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_T10d_DispatcherTimer起動縮退復元_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_T10d_DispatcherTimer%E8%B5%B7%E5%8B%95%E7%B8%AE%E9%80%80%E5%BE%A9%E5%85%83_2026-03-23.md)
  - `T10d` 専用のレビュー観点です。
- [AI向け_レビュー結果_T10d_DispatcherTimer起動縮退復元_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_T10d_DispatcherTimer%E8%B5%B7%E5%8B%95%E7%B8%AE%E9%80%80%E5%BE%A9%E5%85%83_2026-03-23.md)
  - `T10d` の実装返却、fix1、最終レビュー、clean commit、本線取り込み、検証ギャップをまとめた記録です。
- [AI向け_レビュー結果_Q2_StartupFeedRequestコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q2_StartupFeedRequest%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `Q2` を clean worktree で再確認し、現行コードがすでに契約充足していたことと、次 blocker が `WatcherEventQueue` partial であることをまとめた記録です。
- [AI向け_作業指示_Codex_Q3_WatcherEventQueueコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_Q3_WatcherEventQueue%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - clean worktree で `WatcherEventQueue` partial の compile blocker だけを切る次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_Q3_WatcherEventQueueコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_Q3_WatcherEventQueue%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `Q3` 専用のレビュー観点です。
- [AI向け_レビュー結果_Q3_WatcherEventQueueコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q3_WatcherEventQueue%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `WatcherEventQueue` partial を戻して `WatcherRegistration` compile blocker を解消し、次 blocker を `Q4` へ切り分けた記録です。
- [AI向け_作業指示_Codex_Q4_SelectedIndexRepairRequestedコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_Q4_SelectedIndexRepairRequested%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - rescue tab の `SelectedIndexRepairRequested` 公開契約だけを戻す次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_Q4_SelectedIndexRepairRequestedコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_Q4_SelectedIndexRepairRequested%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `Q4` 専用のレビュー観点です。
- [AI向け_レビュー結果_Q4_SelectedIndexRepairRequestedコンパイル不整合解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q4_SelectedIndexRepairRequested%E3%82%B3%E3%83%B3%E3%83%91%E3%82%A4%E3%83%AB%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `RescueTabView` の公開イベント契約を最小追加し、clean worktree では build 成功まで確認した記録です。
- [AI向け_レビュー結果_Q4b_RescueTabView選択救済relay復元_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q4b_RescueTabView%E9%81%B8%E6%8A%9E%E6%95%91%E6%B8%88relay%E5%BE%A9%E5%85%83_2026-03-23.md)
  - `RescueTabView` の選択救済操作 relay 4 本を clean worktree で戻し、本線へ index-only 取り込みした記録です。
- [AI向け_作業指示_Codex_Q5a_WatcherDirectPipelineTests再整合_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_Q5a_WatcherDirectPipelineTests%E5%86%8D%E6%95%B4%E5%90%88_2026-03-23.md)
  - `WatcherRegistrationDirectPipelineTests` を現行 queue 契約へ寄せる次レーンの実装指示書です。
- [AI向け_レビュー指示_Claude_Q5a_WatcherDirectPipelineTests再整合_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_Q5a_WatcherDirectPipelineTests%E5%86%8D%E6%95%B4%E5%90%88_2026-03-23.md)
  - `Q5a` 専用のレビュー観点です。
- [AI向け_レビュー結果_Q5a_WatcherDirectPipelineTests再整合_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q5a_WatcherDirectPipelineTests%E5%86%8D%E6%95%B4%E5%90%88_2026-03-23.md)
  - `Q5a` の fix3、個別 accepted 判定、`Q5b` との帯分離方針をまとめた記録です。
- [AI向け_作業指示_Codex_Q5b_TestCompileDrift小口補正_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_Q5b_TestCompileDrift%E5%B0%8F%E5%8F%A3%E8%A3%9C%E6%AD%A3_2026-03-23.md)
  - manual player / startup ui hang / detail mode / rescue worker args の test drift を小口で直す実装指示書です。
- [AI向け_レビュー指示_Claude_Q5b_TestCompileDrift小口補正_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_Q5b_TestCompileDrift%E5%B0%8F%E5%8F%A3%E8%A3%9C%E6%AD%A3_2026-03-23.md)
  - `Q5b` 専用のレビュー観点です。
- [AI向け_レビュー結果_Q5b_TestCompileDrift小口補正_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q5b_TestCompileDrift%E5%B0%8F%E5%8F%A3%E8%A3%9C%E6%AD%A3_2026-03-23.md)
  - `Q5b` の 4 ファイル補正、x64 build success、review synth accepted をまとめた記録です。
- [AI向け_レビュー結果_Q6_TestsBuild通過と次失敗棚卸し_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q6_TestsBuild%E9%80%9A%E9%81%8E%E3%81%A8%E6%AC%A1%E5%A4%B1%E6%95%97%E6%A3%9A%E5%8D%B8%E3%81%97_2026-03-23.md)
  - `Q5a` / `Q5b` 取り込み後に clean worktree で build/test を回し、次 failing test 群を棚卸しした記録です。
- [AI向け_作業指示_Codex_Q6a_RouterExpectationDrift解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_Q6a_RouterExpectationDrift%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `AutogenRegressionTests` の router expectation drift を、現行 ultra-large 契約へ寄せる実装指示書です。
- [AI向け_レビュー指示_Claude_Q6a_RouterExpectationDrift解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_Q6a_RouterExpectationDrift%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `Q6a` 専用のレビュー観点です。
- [AI向け_レビュー結果_Q6a_RouterExpectationDrift解消_2026-03-23.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_Q6a_RouterExpectationDrift%E8%A7%A3%E6%B6%88_2026-03-23.md)
  - `Q6a` の受け入れ、本線 commit、残留 docs drift をまとめた記録です。
- [AI向け_作業指示_Codex_Q1_ThumbnailQueueビルド不整合解消_2026-03-20.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_Q1_ThumbnailQueue%E3%83%93%E3%83%AB%E3%83%89%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-20.md)
  - `Thumbnail.Queue` の既存ビルド blocker を別レーンで潰すための実装指示書です。
- [AI向け_レビュー指示_Claude_Q1_ThumbnailQueueビルド不整合解消_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_Q1_ThumbnailQueue%E3%83%93%E3%83%AB%E3%83%89%E4%B8%8D%E6%95%B4%E5%90%88%E8%A7%A3%E6%B6%88_2026-03-20.md)
  - `Q1` 差分専用のレビュー観点です。
- [AI向け_配布文面_完成形移行初動3タスク_2026-03-20.md](AI%E5%90%91%E3%81%91_%E9%85%8D%E5%B8%83%E6%96%87%E9%9D%A2_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E5%88%9D%E5%8B%953%E3%82%BF%E3%82%B9%E3%82%AF_2026-03-20.md)
  - 各エージェントへそのまま渡せる依頼文のまとめです。
- [AI向け_レビュー結果_完成形移行初動1回目_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E5%88%9D%E5%8B%951%E5%9B%9E%E7%9B%AE_2026-03-20.md)
  - 初回レビューの finding と、調整役の受け入れ判断をまとめた記録です。
- [AI向け_レビュー結果_完成形移行初動2回目_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E5%88%9D%E5%8B%952%E5%9B%9E%E7%9B%AE_2026-03-20.md)
  - 差し戻し後レビューの結果と、T2 受け入れ / T3 再差し戻しの判断記録です。
- [AI向け_レビュー結果_完成形移行初動3回目_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_%E5%AE%8C%E6%88%90%E5%BD%A2%E7%A7%BB%E8%A1%8C%E5%88%9D%E5%8B%953%E5%9B%9E%E7%9B%AE_2026-03-20.md)
  - T3 再差し戻し後レビューの結果と、初動 3 タスク完了の判断記録です。
- [AI向け_差し戻し指示_Codex_T2_handoff再整理_2026-03-20.md](AI%E5%90%91%E3%81%91_%E5%B7%AE%E3%81%97%E6%88%BB%E3%81%97%E6%8C%87%E7%A4%BA_Codex_T2_handoff%E5%86%8D%E6%95%B4%E7%90%86_2026-03-20.md)
  - T2 の差し戻し修正ポイントです。
- [AI向け_差し戻し指示_Codex_T3_Watcher再整理_2026-03-20.md](AI%E5%90%91%E3%81%91_%E5%B7%AE%E3%81%97%E6%88%BB%E3%81%97%E6%8C%87%E7%A4%BA_Codex_T3_Watcher%E5%86%8D%E6%95%B4%E7%90%86_2026-03-20.md)
  - T3 の差し戻し修正ポイントです。
- [AI向け_差し戻し指示_Codex_T3_Watcher再整理_2回目_2026-03-20.md](AI%E5%90%91%E3%81%91_%E5%B7%AE%E3%81%97%E6%88%BB%E3%81%97%E6%8C%87%E7%A4%BA_Codex_T3_Watcher%E5%86%8D%E6%95%B4%E7%90%86_2%E5%9B%9E%E7%9B%AE_2026-03-20.md)
  - T3 の再差し戻し修正ポイントです。
- [AI向け_作業指示_Codex_LaneB_MainWindowMovieReadFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_LaneB_MainWindowMovieReadFacade_Phase1_2026-03-20.md)
  - Lane B の最初の実装単位として、`MainWindow movie read` を facade 化する指示書です。
- [AI向け_レビュー指示_Claude_LaneB_MainWindowMovieReadFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_LaneB_MainWindowMovieReadFacade_Phase1_2026-03-20.md)
  - T4 差分専用のレビュー観点です。
- [AI向け_レビュー結果_LaneB_MainWindowMovieReadFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_LaneB_MainWindowMovieReadFacade_Phase1_2026-03-20.md)
  - T4 の最終レビュー結果と、受け入れ判断の記録です。
- [AI向け_作業指示_Codex_LaneB_WatchMainDbFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_LaneB_WatchMainDbFacade_Phase1_2026-03-20.md)
  - Lane B の次の実装単位として、watch movie read/write 2 口を facade 化する指示書です。
- [AI向け_レビュー指示_Claude_LaneB_WatchMainDbFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_LaneB_WatchMainDbFacade_Phase1_2026-03-20.md)
  - T5 差分専用のレビュー観点です。
- [AI向け_レビュー結果_LaneB_WatchMainDbFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_LaneB_WatchMainDbFacade_Phase1_2026-03-20.md)
  - T5 のレビュー結果と、Phase1 受け入れ判断の記録です。
- [AI向け_作業指示_Codex_LaneB_MainDbMovieMutationFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_LaneB_MainDbMovieMutationFacade_Phase1_2026-03-20.md)
  - Lane B の 3 本目として、単一 movie 更新入口を facade 化する指示書です。
- [AI向け_レビュー指示_Claude_LaneB_MainDbMovieMutationFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_LaneB_MainDbMovieMutationFacade_Phase1_2026-03-20.md)
  - T6 差分専用のレビュー観点です。
- [AI向け_レビュー結果_LaneB_MainDbMovieMutationFacade_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E7%B5%90%E6%9E%9C_LaneB_MainDbMovieMutationFacade_Phase1_2026-03-20.md)
  - T6 のレビュー結果と、Phase1 受け入れ判断の記録です。
- [AI向け_作業メモ_LaneB_Phase1残課題_2026-03-20.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E3%83%A1%E3%83%A2_LaneB_Phase1%E6%AE%8B%E8%AA%B2%E9%A1%8C_2026-03-20.md)
  - T4 / T5 / T6 の残課題を次サイクルへ切るためのメモです。
- [AI向け_作業指示_Codex_LaneB_FacadeGuardIntegrationTests_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E4%BD%9C%E6%A5%AD%E6%8C%87%E7%A4%BA_Codex_LaneB_FacadeGuardIntegrationTests_Phase1_2026-03-20.md)
  - T7 として、Lane B facade の guard / integration test を補強する指示書です。
- [AI向け_レビュー指示_Claude_LaneB_FacadeGuardIntegrationTests_Phase1_2026-03-20.md](AI%E5%90%91%E3%81%91_%E3%83%AC%E3%83%93%E3%83%A5%E3%83%BC%E6%8C%87%E7%A4%BA_Claude_LaneB_FacadeGuardIntegrationTests_Phase1_2026-03-20.md)
  - T7 差分専用のレビュー観点です。
- [AI向け_CodexCLIサブエージェント運用ガイド_2026-03-20.md](AI%E5%90%91%E3%81%91_CodexCLI%E3%82%B5%E3%83%96%E3%82%A8%E3%83%BC%E3%82%B8%E3%82%A7%E3%83%B3%E3%83%88%E9%81%8B%E7%94%A8%E3%82%AC%E3%82%A4%E3%83%89_2026-03-20.md)
  - `codex CLI` で subagent を使う時の起動手順、custom agent、運用上の注意をまとめた資料です。
- [../Thumbnail/README.md](../Thumbnail/README.md)
  - サムネイル領域の計画、調査、運用資料の入口です。
- [../Watcher/README.md](../Watcher/README.md)
  - Watcher / FileIndex 領域の設計、計画資料の入口です。
- [../AGENTS.md](../AGENTS.md)
  - 作業ルールの基点です。
- [../AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md](../AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md)
  - `workthree` 本線の優先方針です。
- [../AI向け_ブランチ方針_future難読動画実験線_2026-03-11.md](../AI向け_ブランチ方針_future難読動画実験線_2026-03-11.md)
  - `future` 実験線の判断基準です。

## 領域別の資料

### 全体共通

- [ProjectOverview_2026-02-28.md](ProjectOverview_2026-02-28.md)
- [Architecture_2026-02-28.md](Architecture_2026-02-28.md)
- [DB切り替え_最近開いたファイルと新規作成_UI_DB_サムネ常駐処理整理_2026-03-15.md](DB切り替え_最近開いたファイルと新規作成_UI_DB_サムネ常駐処理整理_2026-03-15.md)
  - メニュー起点のMainDB切り替えを、UI / DB / 常駐サムネ処理の3層で整理した現状資料です。
- [ToDo_DB切替_旧Processingジョブ切り離し_2026-03-15.md](ToDo_DB切替_旧Processingジョブ切り離し_2026-03-15.md)
  - DB切替またぎで走る旧 `Processing` ジョブを、後で確実に切るための独立 ToDo です。
- [設計メモ_下部タブ分割方針_2026-03-15.md](設計メモ_下部タブ分割方針_2026-03-15.md)
  - 下部タブをタブごとのフォルダへ分けるための段階的な構想です。
- [Implementation Plan_下部タブ分割_Phase1_サムネ進捗_2026-03-15.md](Implementation Plan_下部タブ分割_Phase1_サムネ進捗_2026-03-15.md)
  - 下部タブ分割の第一歩として、`サムネイル進捗` タブを切る実装計画です。
- [SearchSpec.md](SearchSpec.md)
- [RegressionChecklist.md](RegressionChecklist.md)
- [ToDo.md](ToDo.md)

### サムネイル系

- [../Thumbnail/README.md](../Thumbnail/README.md)
  - `Thumbnail` 配下の計画、調査、運用資料の入口です。
- [../UpperTabs/README.md](../UpperTabs/README.md)
  - 上側タブの設計、実装計画、可視範囲優先の高速化資料の入口です。
- [../UpperTabs/Implementation Plan_上側タブvisible-first高速化_2026-03-15.md](../UpperTabs/Implementation%20Plan_%E4%B8%8A%E5%81%B4%E3%82%BF%E3%83%96visible-first%E9%AB%98%E9%80%9F%E5%8C%96_2026-03-15.md)
  - 上側タブを visible-first で高速化する具体的なフェーズ計画です。
- [ThumbnailEngineRouting_2026-03-01.md](ThumbnailEngineRouting_2026-03-01.md)
  - エンジン切り替え基準の要約です。
- [ffmpeg/README.md](ffmpeg/README.md)
  - FFmpegまわりの調査メモの入口です。

### Watcher / FileIndex 系

- [../Watcher/README.md](../Watcher/README.md)
  - `Watcher` 配下の設計、計画、バグ調査の入口です。

### モデル / DB / スクリプト系

- [../Models/README.md](../Models/README.md)
  - モデル仕様と `MovieInfo` 取得資料の入口です。
- [../DB/README.md](../DB/README.md)
  - DBまわりの障害・設計資料の入口です。
- [../scripts/README.md](../scripts/README.md)
  - 補助スクリプトと手順書の入口です。
- [../Models/MovieInfo_取得値と取得方法.md](../Models/MovieInfo_取得値と取得方法.md)
- [../DB/メインDBスキーマ不一致_不具合内容と対策_2026-03-04.md](../DB/メインDBスキーマ不一致_不具合内容と対策_2026-03-04.md)

## 文書の見分け方

- `Implementation Plan_*.md`
  - 実装前提、作業計画、段取りです。
- `調査結果_*.md`
  - 事象の切り分けと原因分析です。
- `設計メモ_*.md` / `*_Design_*.md`
  - 設計判断や責務境界のメモです。
- `ManualRegressionCheck_*.md` / `RegressionChecklist.md`
  - 手動確認の手順です。
- `_初版.md`
  - 初期スナップショットです。通常は日付付きの新版を優先します。

## 今後の配置ルール

- リポジトリ全体に関わる文書は `Docs` に置く
- サブシステム専用の文書は、関連コードと同じフォルダに置く
- 新しい文書を増やしたら、この案内か各領域の `README.md` を同時に更新する
- 一時メモ、ローカル検証結果、機密を含む資料は `.local` に寄せ、Git管理下へ置かない

## 今回の整理方針

- 既存のファイル移動は行わない
- まず入口を整え、参照切れを避ける
- 大量移動が必要になった場合は、リンク更新を含む別作業として扱う
- トップの `README.md` は歴史資料として残し、現状判断はこの案内を優先する
- AI の現在判断は `../AI向け_現在の全体プラン_workthree_2026-03-20.md` を優先する
