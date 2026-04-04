# scripts ドキュメント案内

このフォルダは、調査や運用を補助する PowerShell スクリプトと、その利用手順を扱います。
基本的に AI / 実装作業向けですが、人が手で実行する時の入口としても使えます。

## 人間向けの入口

- [Implementation Plan_release workflow統合_本命整理_2026-03-30.md](Implementation%20Plan_release%20workflow%E7%B5%B1%E5%90%88_%E6%9C%AC%E5%91%BD%E6%95%B4%E7%90%86_2026-03-30.md)
  - app / worker release workflow を 1 本へ寄せる本命整理の計画です。
- [正式Release手順_GitHubTag運用_2026-03-30.md](正式Release手順_GitHubTag運用_2026-03-30.md)
  - `scripts` 起点で正式 release する時の最短手順です。
- [GEMINI_最近ログTop10抽出手順_2026-03-03.md](GEMINI_最近ログTop10抽出手順_2026-03-03.md)
  - ログから遅い動画を抽出する具体的な実行手順です。

## AI / 実装向けの入口

- [Implementation Plan_release workflow統合_本命整理_2026-03-30.md](Implementation%20Plan_release%20workflow%E7%B5%B1%E5%90%88_%E6%9C%AC%E5%91%BD%E6%95%B4%E7%90%86_2026-03-30.md)
  - 本命整理のタスクリスト付き実装計画です。
- [正式Release手順_GitHubTag運用_2026-03-30.md](正式Release手順_GitHubTag運用_2026-03-30.md)
  - version 更新から tag push まで含めた release 全体の流れです。
- [GEMINI_最近ログTop10抽出手順_2026-03-03.md](GEMINI_最近ログTop10抽出手順_2026-03-03.md)
  - 既存スクリプトを再利用する時の指示テンプレート付きです。
- [bootstrap_private_engine_repo.ps1](bootstrap_private_engine_repo.ps1)
  - Public repo から Private engine repo の初期フォルダ構成、docs、source seed を同期する入口です。
  - `Bootstrap` は初期構成作成、`SyncDocs` は docs 同期、`SyncSource` は 4 project + Images/tools + solution / workflow / smoke test seed を同期します。
- [sync_private_engine_worker_artifact.ps1](sync_private_engine_worker_artifact.ps1)
  - Private repo の `private-engine-publish` artifact を Public repo の `artifacts/rescue-worker/publish/Release-win-x64` へ同期する入口です。
  - `git credential` から GitHub token を取得し、最新成功 run または指定 run の artifact を展開します。
  - 同期先には `rescue-worker-sync-source.json` も書き、app package 側が external artifact 起点で lock 情報を残せるようにします。

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
- `bootstrap_private_engine_repo.ps1`
  - Private repo の初期フォルダを作り、docs / source / workflow / smoke test seed を同期します。
- `sync_private_engine_worker_artifact.ps1`
  - Private repo の publish artifact を Public repo へ同期し、launcher が publish artifact 優先で拾える状態へ寄せます。

## 配置ルール

- 反復実行する調査手順は、スクリプトと同じフォルダに手順書を置く
- ローカル依存の既定値を増やす時は、README と手順書で明示する
