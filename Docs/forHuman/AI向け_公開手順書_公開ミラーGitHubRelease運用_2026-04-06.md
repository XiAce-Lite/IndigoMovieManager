# AI向け 公開手順書 公開ミラー GitHub Release 運用 2026-04-06

## この文書の目的

この文書は、AI が `IndigoMovieManager_fork` 側で公開ミラー運用の preview / release を実施する時の正面入口である。
復旧判断や障害時の深掘りは別紙を使い、ここでは通常運用の最短手順だけを扱う。

関連文書:

- `Docs/forHuman/AI向け_公開ミラーGitHubRelease復旧手順_run_id_release_tag_2026-04-06.md`
- `Docs/forHuman/continu計画_エンジン外だし完了後_本家PRまで_2026-04-06.md`

## 対象リポジトリ

- Public app repo: `T-Hamada0101/IndigoMovieManager_fork`
- Public mirror repo: `T-Hamada0101/IndigoMovieEngine-Mirror`
- Private engine repo: `T-Hamada0101/IndigoMovieEngine`

## 役割分担

- `IndigoMovieManager_fork`
  - app package を作る
  - WiX installer を作る
  - GitHub Release へ `ZIP + installer exe` を公開する
- `IndigoMovieEngine-Mirror`
  - worker zip や engine package release asset を公開側から見せる入口
- `IndigoMovieEngine`
  - worker / packages artifact の正本
  - run_id 経路の正本

## 重要な前提

- `release_tag` は GitHub Release asset を探す
- `run_id` は GitHub Actions artifact を探す
- `run_id` の正本は private repo であり、mirror repo ではない
- mirror release asset が不完全でも、現在の workflow は private repo と run pin へ fallback する

## 必須設定

`IndigoMovieManager_fork` 側で以下を設定しておく。

- Repository Variable
  - `PUBLIC_ENGINE_MIRROR_REPO = T-Hamada0101/IndigoMovieEngine-Mirror`
  - `PRIVATE_ENGINE_PUBLISH_RUN_ID = 23997659256`
- Repository Secret
  - `INDIGO_ENGINE_REPO_TOKEN`

## 正面入口 workflow

- `.github/workflows/github-release-package.yml`

この workflow は以下を行う。

1. worker を同期する
2. private engine packages を同期する
3. app package zip を作る
4. WiX installer exe を作る
5. artifact を upload する
6. tag 実行時は GitHub Release へ公開する

## AI が最初に選ぶべき実行方法

### 1. 通常の preview 確認

最初は `workflow_dispatch` を入力なしで実行する。

理由:

- `PRIVATE_ENGINE_PUBLISH_RUN_ID` が既定で使われる
- release asset の揺れに影響されにくい
- 2026-04-06 時点で成功確認済み

### 2. release tag を明示したい時

`private_engine_release_tag=<tag>` を指定して `workflow_dispatch` を実行する。

例:

```text
private_engine_release_tag=v1.0.3.5
```

現在の workflow は次の順で自動 fallback する。

1. public mirror release
2. private repo release
3. `PRIVATE_ENGINE_PUBLISH_RUN_ID`

### 3. run を明示したい時

`private_engine_run_id=<runId>` を指定する。

ただし、worker と packages の両 artifact を持つ private run だけを使う。
迷ったら `23997659256` を基準にする。

## tag push 時の扱い

tag push でも `github-release-package.yml` は起動する。

tag 実行時は最終的に GitHub Release へ以下を公開する。

- `IndigoMovieManager-<version>-win-x64.zip`
- `IndigoMovieManager-Setup-<version>-win-x64.exe`

ただし、公開 release の前に preview と同じ同期処理を通るため、事前に `workflow_dispatch` で成功確認してから tag push する方が安全である。

## 成功確認済み実行

- `24032239061`
  - `private_engine_run_id=23997659256`
- `24032342401`
  - 入力なし `workflow_dispatch`
- `24032500601`
  - `private_engine_release_tag=v1.0.3.5`

## AI 用の最短判断

- まずは入力なし `workflow_dispatch`
- 失敗したら `PRIVATE_ENGINE_PUBLISH_RUN_ID` を確認
- release 再現確認が必要なら `private_engine_release_tag`
- deep fallback 判断が必要なら復旧手順書を見る

## やってはいけないこと

- mirror repo の release が見えるだけで `run_id` も使えると判断しない
- worker だけある run を `PRIVATE_ENGINE_PUBLISH_RUN_ID` に設定しない
- packages 不足の release tag を見て workflow 全体の修正が必要だと早合点しない
- private repo の token が必要な場面と不要な場面を混同しない

## 次に private 側で run を更新した時の更新手順

1. private repo の成功 run を確認する
2. `rescue-worker-publish` と `private-engine-packages` の両 artifact があることを確認する
3. `IndigoMovieManager_fork` の `PRIVATE_ENGINE_PUBLISH_RUN_ID` をその run id に更新する
4. `workflow_dispatch` を入力なしで 1 回実行して成功確認する

## 補足

障害が再発した場合は、通常手順書ではなく復旧手順書を見る。
復旧時の正本は以下である。

- `Docs/forHuman/AI向け_公開ミラーGitHubRelease復旧手順_run_id_release_tag_2026-04-06.md`
