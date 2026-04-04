# Release本番前チェックリスト private-engine連携 2026-04-04

最終更新日: 2026-04-04

## 1. 目的

- Public repo の app release が、Private repo の worker publish artifact を pin して取り込む前提で安全に流せるかを、本番前に短く確認する

## 2. 今回の前提値

- Public repo: `T-Hamada0101/IndigoMovieManager_fork`
- Private repo: `T-Hamada0101/IndigoMovieEngine`
- Private publish run id: `23966594219`
- compatibilityVersion: `2026-03-17.1`
- preview 成功 run: `23978177837`

## 3. GitHub Settings

- Public repo secret `INDIGO_ENGINE_REPO_TOKEN` が設定済み
- Public repo variable `PRIVATE_ENGINE_PUBLISH_RUN_ID=23966594219` が設定済み

## 4. release 前ローカル確認

- `IndigoMovieManager.csproj` の version が次の tag と一致する
- `scripts/sync_private_engine_worker_artifact.ps1 -RunId 23966594219` が通る
- `scripts/create_github_release_package.ps1 -PreparedWorkerPublishDir artifacts/rescue-worker/publish/Release-win-x64` が通る
- `dotnet msbuild IndigoMovieManager.sln /p:Configuration=Release /p:Platform=x64` が通る

## 5. workflow 確認

- `github-release-package.yml` は `private_engine_run_id` を受け取れる
- `private_engine_run_id` 未指定時も `PRIVATE_ENGINE_PUBLISH_RUN_ID` で pin できる
- token または run id が無い時は local worker build へ fallback する

## 6. 成功ログで見る行

- `Private worker artifact synced.`
- `repo: T-Hamada0101/IndigoMovieEngine`
- `runId: 23966594219`
- `artifact: rescue-worker-publish`
- `compatibilityVersion: 2026-03-17.1`
- `worker lock verification ok`

## 7. release 後に見るもの

- GitHub Release asset は app ZIP のみ
- Release 本文先頭に worker lock summary が出る
- package 内 `rescue-worker.lock.json` の `sourceType` が `artifact-sync`

