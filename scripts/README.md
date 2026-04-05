# scripts ドキュメント案内

このフォルダは、調査や運用を補助する PowerShell スクリプトと、その利用手順を扱います。
基本的に AI / 実装作業向けですが、人が手で実行する時の入口としても使えます。

## 人間向けの入口

- [Implementation Plan_release workflow統合_本命整理_2026-03-30.md](Implementation%20Plan_release%20workflow%E7%B5%B1%E5%90%88_%E6%9C%AC%E5%91%BD%E6%95%B4%E7%90%86_2026-03-30.md)
  - app / worker release workflow を 1 本へ寄せる本命整理の計画です。
- [正式Release手順_GitHubTag運用_2026-03-30.md](正式Release手順_GitHubTag運用_2026-03-30.md)
  - `scripts` 起点で正式 release する時の最短手順です。
- [Release本番前チェックリスト_private-engine連携_2026-04-04.md](Release%E6%9C%AC%E7%95%AA%E5%89%8D%E3%83%81%E3%82%A7%E3%83%83%E3%82%AF%E3%83%AA%E3%82%B9%E3%83%88_private-engine%E9%80%A3%E6%90%BA_2026-04-04.md)
  - private engine artifact pin を使う release 前に、GitHub Settings と成功ログの見る場所を短く確認するチェックリストです。
- [Implementation Plan_WiXv6再検討_GitHub連携_VSCode最新前提_2026-04-05.md](Implementation%20Plan_WiXv6%E5%86%8D%E6%A4%9C%E8%A8%8E_GitHub%E9%80%A3%E6%90%BA_VSCode%E6%9C%80%E6%96%B0%E5%89%8D%E6%8F%90_2026-04-05.md)
  - installer 正本計画です。WiX v6 を正式採用し、v1 は install / upgrade / uninstall に絞っています。2026-04-05 時点では `ZIP + bundle exe` を同じ release へ載せる導線まで着手済みです。
- [仕様書_WiXv6インストーラーと自己更新_2026-04-05.md](%E4%BB%95%E6%A7%98%E6%9B%B8_WiXv6%E3%82%A4%E3%83%B3%E3%82%B9%E3%83%88%E3%83%BC%E3%83%A9%E3%83%BC%E3%81%A8%E8%87%AA%E5%B7%B1%E6%9B%B4%E6%96%B0_2026-04-05.md)
  - WiX の実装仕様書です。`v1: install / upgrade / uninstall`、`v2: self-update`、`v3: custom BA` の順で読む正本です。
- [Implementation Plan_InnoSetupインストーラー導入_2026-04-05.md](Implementation%20Plan_InnoSetup%E3%82%A4%E3%83%B3%E3%83%8ESetup%E3%82%A4%E3%83%B3%E3%82%B9%E3%83%88%E3%83%BC%E3%83%A9%E3%83%BC%E5%B0%8E%E5%85%A5_2026-04-05.md)
  - Inno Setup 版の比較・履歴です。正式な installer 正本は WiX v6 側を見ます。
- [GEMINI_最近ログTop10抽出手順_2026-03-03.md](GEMINI_最近ログTop10抽出手順_2026-03-03.md)
  - ログから遅い動画を抽出する具体的な実行手順です。

## AI / 実装向けの入口

- [Implementation Plan_release workflow統合_本命整理_2026-03-30.md](Implementation%20Plan_release%20workflow%E7%B5%B1%E5%90%88_%E6%9C%AC%E5%91%BD%E6%95%B4%E7%90%86_2026-03-30.md)
  - 本命整理のタスクリスト付き実装計画です。
- [Implementation Plan_WiXv6再検討_GitHub連携_VSCode最新前提_2026-04-05.md](Implementation%20Plan_WiXv6%E5%86%8D%E6%A4%9C%E8%A8%8E_GitHub%E9%80%A3%E6%90%BA_VSCode%E6%9C%80%E6%96%B0%E5%89%8D%E6%8F%90_2026-04-05.md)
  - installer 正本計画です。WiX v6 採用後の v1/v2/v3 境界と、既存 app package lock 継承、release workflow への接続前提を見ます。
- [仕様書_WiXv6インストーラーと自己更新_2026-04-05.md](%E4%BB%95%E6%A7%98%E6%9B%B8_WiXv6%E3%82%A4%E3%83%B3%E3%82%B9%E3%83%88%E3%83%BC%E3%83%A9%E3%83%BC%E3%81%A8%E8%87%AA%E5%B7%B1%E6%9B%B4%E6%96%B0_2026-04-05.md)
  - installer 仕様の正本です。受け入れ条件と実装順も `v1 / v2 / v3` で揃えています。
- [正式Release手順_GitHubTag運用_2026-03-30.md](正式Release手順_GitHubTag運用_2026-03-30.md)
  - version 更新から tag push まで含めた release 全体の流れです。
- [GEMINI_最近ログTop10抽出手順_2026-03-03.md](GEMINI_最近ログTop10抽出手順_2026-03-03.md)
  - 既存スクリプトを再利用する時の指示テンプレート付きです。
- [設計メモ_bootstrap_private_engine_repo引退_2026-04-05.md](設計メモ_bootstrap_private_engine_repo引退_2026-04-05.md)
  - Public repo から bootstrap / seed を引退させ、Private repo clone を唯一の初期化入口へ寄せた判断メモです。
- `Private repo: %USERPROFILE%\source\repos\IndigoMovieEngine\docs\運用ガイド_PrivateEngine初期化とrelease運用_2026-04-05.md`
  - Private repo 側の正面運用入口です。通常の build / publish / worker release は、こちらを正本にします。
- `Private repo: %USERPROFILE%\source\repos\IndigoMovieEngine\docs\運用ガイド_PrivateEngine_compatibilityVersion_preview_rollback_2026-04-05.md`
  - `compatibilityVersion` bump、preview pin、rollback 判断の正本です。
- [sync_private_engine_worker_artifact.ps1](sync_private_engine_worker_artifact.ps1)
  - Private repo の `private-engine-publish` artifact を Public repo の `artifacts/rescue-worker/publish/Release-win-x64` へ同期する入口です。
  - `-GitHubToken` / `IMM_PRIVATE_ENGINE_TOKEN` / `GH_TOKEN` / `GITHUB_TOKEN` / `git credential` の順で token を解決し、最新成功 run または指定 run の artifact を展開します。
  - 同期先には `rescue-worker-sync-source.json` も書き、app package 側が external artifact 起点で lock 情報を残せるようにします。
  - 2026-04-04 時点で、Public repo の preview workflow から run `23966594219` を使った live 同期成功を確認済みです。
- [sync_private_engine_packages.ps1](sync_private_engine_packages.ps1)
  - Private repo の `Contracts / Engine / FailureDb` package を Public repo の `artifacts/private-engine-packages/Release` へ同期する入口です。
  - `-ReleaseTag` では GitHub Release asset を、`-RunId` では `private-engine-packages` artifact を同期します。
  - 同期先には `private-engine-packages-source.json` も書き、release helper が package version を機械的に解決できるようにします。
- [test_private_engine_package_consume.ps1](test_private_engine_package_consume.ps1)
  - Private repo で pack した `Contracts / Engine / FailureDb` を、Public repo が package consume mode で実際に飲めるかを local で検証する入口です。
  - package source を省略した時は `%USERPROFILE%\source\repos\IndigoMovieEngine\artifacts\private-engine-packages\<Configuration>` を既定で使います。
  - package version を省略した時は feed 内の `Contracts / Engine / FailureDb` から共通 version を自動解決します。

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
  - `PreparedWorkerPublishDir` の worker publish と、`PreparedPrivateEnginePackageDir` の `Contracts / Engine / FailureDb` package を使う app package 専用です。
  - worker / package のどちらも local source build へ戻らず、同期済み Private 正本が無ければ fail-fast します。
- `create_wix_installer_from_release_package.ps1`
  - `create_github_release_package.ps1` が作った verify 済み app package を唯一入力にして、`MSI + Burn bundle exe` を作ります。
  - worker / package provenance は package 内 `rescue-worker.lock.json` と `privateEnginePackages` を read-only で継承します。
  - v1 は per-user install proof 優先のため `SuppressValidation=true` を使います。
- `invoke_release.ps1`
  - clean worktree 前提で version 更新から tag push までを束ねます。
  - Public repo の正面入口として、同期済み `PreparedWorkerPublishDir` と `PreparedPrivateEnginePackageDir` を使う app release 専用です。
  - app package 作成後に、その verified package を入力に WiX bundle exe も作ります。
  - 既定の同期先は `artifacts/rescue-worker/publish/Release-win-x64` と `artifacts/private-engine-packages/Release` で、どちらかが無ければ fail-fast します。
- package consume mode
  - `ImmUsePrivateEnginePackages=true` を付けると、app / queue / runtime / tests が参照する `Contracts / Engine / FailureDb` を `PackageReference` へ切り替えます。
  - `Queue / Runtime` 自体は Public repo 側 project のまま残し、Private package 化するのは shared core だけです。
  - feed は `ImmPrivateEnginePackageSource`、version は `ImmPrivateEnginePackageVersion` でまとめて切り替えられます。
  - 個別にずらしたい時だけ `ImmThumbnailContractsPackageVersion` などの個別 property を上書きします。
  - 日常の確認は `test_private_engine_package_consume.ps1` を正面入口にして、手打ちコマンドの再構成を避けます。
- `sync_private_engine_worker_artifact.ps1`
  - Private repo の release asset または publish artifact を Public repo へ同期し、launcher が publish artifact 優先で拾える状態へ寄せます。
  - `-ReleaseTag` を渡した時は private release asset を正本として扱い、`-RunId` は preview 用の publish artifact ルートとして残します。
- `invoke_github_release_preview.ps1`
  - `GH_TOKEN` / `GITHUB_TOKEN` / `git credential` の順で token を取り、preview workflow を手元から叩きます。
  - `-PrivateEngineRunId` を付けると preview 側へ private publish run pin を渡せます。
  - `-PrivateEngineReleaseTag` を付けると private release asset を preview 側で明示選択できます。
- `.github/workflows/github-release-package.yml`
  - `v*` tag push では private repo の worker release asset と engine package release asset を tag 名で同期してから app package を作ります。
  - その verify 済み app package を入力に WiX bundle exe も作り、release asset に `ZIP + bundle exe` を並べます。
  - `workflow_dispatch` では `private_engine_release_tag` で release asset、`private_engine_run_id` で publish artifact を選べます。
  - Public workflow は local worker source build へ戻らず、Private source が取れない時点で fail-fast します。
  - 2026-04-05 に preview run `23993264073` で `private_engine_release_tag=v1.0.3.5-private.2` の live 成功を確認しました。
  - 2026-04-05 に preview run `23995516296` で WiX installer 追加後の live 成功を確認し、`github-release-package / github-release-installer / github-release-body-preview` の 3 artifact が並ぶことを確認しました。
  - 2026-04-05 に preview run `23982259537` で `private_engine_release_tag=v1.0.3.5` の live 成功を確認しました。
  - 2026-04-04 に Public repo で `INDIGO_ENGINE_REPO_TOKEN` + `PRIVATE_ENGINE_PUBLISH_RUN_ID=23966594219` を設定し、preview run `23978177837` の live 成功を確認しました。
  - 2026-04-04 に preview run `23979016211` で `private_engine_release_tag=v1.0.3.4-private.1` の live 成功も確認しました。
  - 2026-04-04 に tag run `23979520980` / release `v1.0.3.5` で private release asset 正本ルートの本番成功も確認しました。
- worker 単体確認の正本入口
  - Private repo の `private-engine-publish` を手動実行します。
  - local で worker ZIP が必要な時も、Private repo 側の `scripts\create_rescue_worker_artifact_package.ps1` を使います。
  - Public repo 側は app package を配る責務へ集中し、worker 単体確認 workflow は持ちません。
- bootstrap / seed の扱い
  - 2026-04-05 に Public repo から `bootstrap_private_engine_repo.ps1` と `scripts\private-engine-seed\` を引退させました。
  - Private repo の初期化は clone + Private repo docs を正本にします。

## 配置ルール

- 反復実行する調査手順は、スクリプトと同じフォルダに手順書を置く
- ローカル依存の既定値を増やす時は、README と手順書で明示する
