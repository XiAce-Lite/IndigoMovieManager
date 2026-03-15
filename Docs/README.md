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
- [SearchSpec.md](SearchSpec.md)
- [RegressionChecklist.md](RegressionChecklist.md)
- [ToDo.md](ToDo.md)

### サムネイル系

- [../Thumbnail/README.md](../Thumbnail/README.md)
  - `Thumbnail` 配下の計画、調査、運用資料の入口です。
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
