# AI向け 公開ミラー GitHub Release 復旧手順 run_id / release_tag 2026-04-06

関連文書:

- `Docs/forHuman/AI向け_公開手順書_公開ミラーGitHubRelease運用_2026-04-06.md`
- `Docs/forHuman/continu計画_エンジン外だし完了後_本家PRまで_2026-04-06.md`

## この文書の目的

2026-04-06 に、公開ミラー運用の `github-release-package.yml` が AI により何度も失敗した。
原因は `private_engine_release_tag` と `private_engine_run_id` の使い分け、および公開ミラーと private repo の役割分担が曖昧だったためである。

この文書は、次回以降 AI が同じ地雷を踏まないための最短手順と既定値をまとめる。

## まず覚えること

- `release_tag` は「GitHub Release asset を探す経路」である
- `run_id` は「GitHub Actions artifact を探す経路」である
- 公開ミラー `T-Hamada0101/IndigoMovieEngine-Mirror` は release asset 用の入口であり、`run_id` の正本ではない
- `run_id` 経路は private repo `T-Hamada0101/IndigoMovieEngine` を見る
- worker と packages の両方を持つ private run を pin しないと失敗する

## 2026-04-06 時点の正しい既定値

- `PUBLIC_ENGINE_MIRROR_REPO = T-Hamada0101/IndigoMovieEngine-Mirror`
- `PRIVATE_ENGINE_PUBLISH_RUN_ID = 23997659256`

`23997659256` は、以下 2 つの artifact を両方持つ成功 run である。

- `rescue-worker-publish`
- `private-engine-packages`

## 使ってはいけない値

- `PRIVATE_ENGINE_PUBLISH_RUN_ID = 23966594219`

この run は worker 側には使えても、packages 側 artifact `private-engine-packages` が無く、以下の失敗を起こす。

```text
artifact が見つかりません: runId=23966594219 name=private-engine-packages
```

## 2026-04-06 に起きた失敗の整理

### 1. 公開ミラー release には worker 名の揺れがあった

公開ミラー `v1.0.3.5` には新命名の worker zip が最初は無く、以下で失敗した。

```text
release asset が見つかりません:
expectedPattern=^IndigoMovieManager\.Thumbnail\.RescueWorker-v1\.0\.3\.5-win-x64-compat-.*\.zip$
```

対策:

- `scripts/sync_private_engine_worker_artifact.ps1` で旧名 `IndigoMovieManager-v1.0.3.5-win-x64.zip` を fallback で許容済み

### 2. 公開ミラー release には packages が揃っていなかった

公開ミラー `v1.0.3.5` には packages が足りず、以下で失敗した。

```text
release asset が一意に見つかりません: IndigoMovieEngine.Thumbnail.Contracts.1.0.3.5.nupkg
```

対策:

- workflow で `公開ミラー -> private release -> run_id pin` の順に fallback するよう修正済み

### 3. run_id 経路で mirror repo を見て 404 になっていた

失敗時の workflow は `private_engine_run_id` を指定しても mirror repo を見ており、以下で失敗していた。

```text
Not Found
https://docs.github.com/rest/actions/workflow-runs#get-a-workflow-run
```

対策:

- `run_id` 経路は private repo 固定へ修正済み

## 現在の workflow 挙動

`.github/workflows/github-release-package.yml` は以下の順で同期を試みる。

### `private_engine_release_tag` を使う時

1. 公開ミラー release
2. private repo release
3. `PRIVATE_ENGINE_PUBLISH_RUN_ID` の run artifact

### `private_engine_run_id` を使う時

1. private repo の run artifact

## AI が最初に試すべき手順

### 通常の preview / 手動実行

何も考えず、まずは入力なしの `workflow_dispatch` を使う。
この時は repo variable の `PRIVATE_ENGINE_PUBLISH_RUN_ID` が使われる。

### release tag を明示したい時

`private_engine_release_tag` を指定してよい。
ただし release asset が mirror / private の両方で不完全でも、現在は `PRIVATE_ENGINE_PUBLISH_RUN_ID` へ自動退避する。

### run_id を明示したい時

worker と packages の両 artifact を持つ private run だけを使う。
迷ったら `23997659256` を基準にする。

## 失敗時の切り分け順

1. `run_id` を見ているのか `release_tag` を見ているのかを確認する
2. `run_id` なら private repo の run か確認する
3. その run に `rescue-worker-publish` と `private-engine-packages` の両方があるか確認する
4. `release_tag` なら mirror release に worker zip と nupkg 3 点が揃っているか確認する
5. 揃っていなければ private release を見る
6. それでも揃っていなければ `PRIVATE_ENGINE_PUBLISH_RUN_ID` へ退避する

## 成功確認済み run

- `24032239061`
  - `private_engine_run_id=23997659256` で成功
- `24032342401`
  - 入力なし `workflow_dispatch` で成功
- `24032500601`
  - `private_engine_release_tag=v1.0.3.5` で成功

## AI への運用指示

- 公開ミラーの release asset が不完全でも、すぐ workflow を疑わず `PRIVATE_ENGINE_PUBLISH_RUN_ID` を確認する
- 新しい private publish 成功 run ができたら、worker と packages の両 artifact を持つことを確認してから `PRIVATE_ENGINE_PUBLISH_RUN_ID` を更新する
- `run_id` と `release_tag` を混同しない
- 公開ミラーは「release asset を見せる入口」であり、「artifact の正本」ではない
