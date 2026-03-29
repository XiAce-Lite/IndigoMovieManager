# scripts ドキュメント案内

このフォルダは、調査や運用を補助する PowerShell スクリプトと、その利用手順を扱います。
基本的に AI / 実装作業向けですが、人が手で実行する時の入口としても使えます。

## 人間向けの入口

- [正式Release手順_GitHubTag運用_2026-03-30.md](正式Release手順_GitHubTag運用_2026-03-30.md)
  - `scripts` 起点で正式 release する時の最短手順です。
- [GEMINI_最近ログTop10抽出手順_2026-03-03.md](GEMINI_最近ログTop10抽出手順_2026-03-03.md)
  - ログから遅い動画を抽出する具体的な実行手順です。

## AI / 実装向けの入口

- [正式Release手順_GitHubTag運用_2026-03-30.md](正式Release手順_GitHubTag運用_2026-03-30.md)
  - version 更新から tag push まで含めた release 全体の流れです。
- [GEMINI_最近ログTop10抽出手順_2026-03-03.md](GEMINI_最近ログTop10抽出手順_2026-03-03.md)
  - 既存スクリプトを再利用する時の指示テンプレート付きです。

## 現状の主要スクリプト (2026-03-12)

- `export_thumbnail_log_summary.ps1`
  - サムネイル処理ログの要約を出力します。
- `export_thumbnail_rescue_summary.ps1`
  - 救済処理系の集計補助です。
- `find_slow_videos.ps1`
  - 重い動画の抽出補助です。
- `run_fileindex_ab_ci.ps1`
  - FileIndex の比較実行補助です。
- `everythinglite_sync.ps1`
  - EverythingLite 周辺の同期補助です。
- `Check-Mojibake.ps1`
  - 文字化け確認の補助です。
- `create_github_release_package.ps1`
  - 本体 app の配布 ZIP を作ります。
- `create_rescue_worker_artifact_package.ps1`
  - rescue worker の個別 artifact ZIP を作ります。
- `invoke_release.ps1`
  - clean worktree 前提で version 更新から tag push までを束ねます。

## 配置ルール

- 反復実行する調査手順は、スクリプトと同じフォルダに手順書を置く
- ローカル依存の既定値を増やす時は、README と手順書で明示する
