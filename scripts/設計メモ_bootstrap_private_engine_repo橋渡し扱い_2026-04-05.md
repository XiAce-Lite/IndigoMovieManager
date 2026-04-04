# 設計メモ bootstrap_private_engine_repo 橋渡し扱い 2026-04-05

最終更新日: 2026-04-05

## 1. 目的

`bootstrap_private_engine_repo.ps1` を、通常運用の入口ではなく
「Public repo から Private repo を初期化・再同期する橋渡し資産」として固定する。

Public repo は app に機能を追加し、配る責務へ集中する。
そのため、この script を日常運用の release 導線や worker 配布導線へ混ぜない。

## 2. 現在の位置づけ

- 所在: `scripts/bootstrap_private_engine_repo.ps1`
- seed 正本: `scripts/private-engine-seed/`
- 用途:
  - Private repo 初期作成
  - Private repo docs / source / workflow の再同期
  - Public 側の current state から Private seed を再生成する補助

これは runtime の実行経路でも、release 本番経路でもない。

## 3. なぜ Public repo に残すか

現時点では次の理由で Public repo に残す価値がある。

1. 外だし移行の途中で、Public 側の current state から Private repo seed を再構成できる
2. docs / seed / bootstrap の整合を 1 か所で確認しやすい
3. Private repo を作り直す時の入口が明確で、初回セットアップの再現性を保てる

つまり、これは main 運用の正面入口ではなく、移行期間の再現性を担保する保険である。

## 4. 通常運用で使わないもの

この script を次へ流用しない。

- app release
- worker release
- worker artifact 正常同期
- launcher runtime 起動
- 日常の CI publish

通常運用は次を正本にする。

- Public repo:
  - `scripts/invoke_release.ps1`
  - `scripts/create_github_release_package.ps1`
  - `.github/workflows/github-release-package.yml`
- Private repo:
  - `scripts/publish_private_engine.ps1`
  - `scripts/create_rescue_worker_artifact_package.ps1`
  - `.github/workflows/private-engine-publish.yml`
  - `docs/運用ガイド_PrivateEngine初期化とrelease運用_2026-04-05.md`
  - `docs/運用ガイド_PrivateEngine_compatibilityVersion_preview_rollback_2026-04-05.md`

## 5. 引退条件

次を満たしたら、`bootstrap_private_engine_repo.ps1` は縮退または削除候補にしてよい。

1. Private repo 側だけで初期セットアップ手順が完結している
2. Private repo 側 docs / workflow / seed が Public repo からの再生成を前提にしていない
3. Public repo 側の current state を seed 元として参照しなくても、Private repo の更新運用が安定して回る
4. 新規環境での Private repo 構築を、手順書と Private repo 正本だけで再現できる
5. 少なくとも数回の release / preview で bootstrap 再実行が不要だった

## 6. いまの判断

2026-04-05 時点では、まだ削除しない。

理由:

- `scripts/private-engine-seed/` を Public 側で保持しており、seed 正本の整理は済んだが
  まだ「Private repo 単独で seed 更新を閉じる」段までは行っていない
- 外だし移行の最終盤で、再現性の高い bridge を残しておく方が安全

したがって当面の扱いは次とする。

- 残す
- ただし通常運用ルートには載せない
- README / 実行時 warning で bridge であることを明示する
- Private repo 側の正面運用 docs を育て、Public 側は参照リンク中心へ寄せる

補足:

- 引退条件の実績評価は
  `scripts/設計メモ_bootstrap_private_engine_repo引退条件評価_2026-04-05.md`
  を正本にする
- 2026-04-05 時点の評価では、通常運用条件はかなり満たしているが
  `scripts/private-engine-seed/` の正本所有がまだ Public 側にあるため、
  bridge 資産としては残す判断である

## 7. 参照

- `scripts/README.md`
- `scripts/設計メモ_bootstrap_private_engine_repo引退条件評価_2026-04-05.md`
- `Thumbnail/Docs/Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/TASK-008_main repo残置責務とexternal worker運用_2026-04-03.md`
