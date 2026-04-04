# GitHub Release 実行可能バイナリ配布手順 2026-03-15

最終更新日: 2026-04-05

## 1. 方針

- 実行可能バイナリは Git の履歴へ直接 commit しない
- 配布物は GitHub の Release へ ZIP を添付する
- `exe` 単体ではなく、publish 出力一式を ZIP 化して配布する

## 2. このリポジトリで追加したもの

- `scripts/create_github_release_package.ps1`
  - PowerShell 7 前提の配布 ZIP 生成スクリプト
- `scripts/verify_app_package_worker_lock.ps1`
  - app package 内の `rescue-worker.lock.json` と同梱 worker の整合を確認する smoke script
- `scripts/create_rescue_worker_artifact_package.ps1`
  - rescue worker artifact 用 ZIP 生成スクリプト
- `scripts/invoke_release.ps1`
  - clean worktree 前提で version 更新から tag push までを束ねる release helper
  - app package 作成後、`artifacts/github-release` 直下へ GitHub Release 本文へ貼りやすい worker lock 要約 markdown も書き出す
- `scripts/sync_private_engine_worker_artifact.ps1`
  - Private repo の `private-engine-publish` artifact または private release asset を Public repo の publish 置き場へ同期する helper
- `.github/workflows/github-release-package.yml`
  - `v*` tag push では private release asset を tag 名で同期してから app package を作る
  - `workflow_dispatch` では `private_engine_release_tag` で release asset、`private_engine_run_id` で private publish run を固定できる
- `scripts/invoke_github_release_preview.ps1`
  - `GH_TOKEN` または `GITHUB_TOKEN` があれば、`github-release-package.yml` を `workflow_dispatch` で叩いて preview run の URL まで追える helper
  - `-PrivateEngineReleaseTag` で private release asset を preview 側へ明示固定できる
- `.github/workflows/github-release-package.yml`
  - `v*` タグ push で app ZIP を GitHub Release へ添付する正本 workflow
  - `release-worker-lock-summary-*.md` を `body_path` で読み、worker pin 情報を Release 本文先頭へ入れる
  - `workflow_dispatch` でも `release-worker-lock-summary-*.md` を artifact として残し、GitHub 上で本文 preview を確認できる
  - Actions の run summary にも同じ markdown を表示し、artifact を開かなくても本文 preview を見られる
- worker 単体確認の入口
  - Private repo の `private-engine-publish` を手動実行する

## 3. ローカルで ZIP を作る手順

正式 release を最短で進めたい場合は、まず次も選べる。

```powershell
./scripts/sync_private_engine_worker_artifact.ps1 -ReleaseTag v1.0.0
./scripts/invoke_release.ps1 -Version 1.0.0.0
```

この helper は、clean worktree 前提で
- version 更新
- Release build
- app package 作成
- commit
- branch push
- tag 作成
- tag push
まで進める。

補足:
- `-AllowDirty` を使う時でも staged 変更は空であることが必要
- branch push と tag push を両方行う時は atomic push を使う
- `invoke_release.ps1` は app package 作成後に `rescue-worker.lock.json` を読み、`source / version / asset / compatibilityVersion / sha256` を表示する
- `create_github_release_package.ps1` は `artifacts/github-release/release-worker-lock-summary-<version>-<runtime>.md` も書き出す
- `invoke_release.ps1` はその summary を使う前提で、同じ pin 情報を console へも表示する
- `sync_private_engine_worker_artifact.ps1` で同期した publish artifact または release asset は、`-PreparedWorkerPublishDir` 指定時だけ app package へ同梱できる
- `invoke_release.ps1` は prepared worker artifact を使う app release 専用入口である
- local worker source build は `create_github_release_package.ps1` / `create_rescue_worker_artifact_package.ps1` の下位 script 側に閉じ込める
- Public repo の GitHub Actions でも、`INDIGO_ENGINE_REPO_TOKEN` secret が入っていれば同じ同期導線を自動で使える
- ただし preview の run-id route は残しつつ、tag release では private release asset を tag 名で引く
- この summary markdown には、GitHub Release 本文へそのまま貼る block も入る
- この markdown は `GitHub Release 本文へ貼るブロック` と `ローカル確認用` を持ち、貼り付け用 block 内では `### Bundled Rescue Worker` と `Source / Version / Artifact / CompatibilityVersion / WorkerExe SHA256` の最小項目だけを持つ
- tag release では GitHub Actions がこの summary markdown を `body_path` として使い、worker pin 情報を Release 本文先頭へ自動反映する

PowerShell 7 でリポジトリ直下へ移動して実行する。

```powershell
./scripts/create_github_release_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot artifacts/github-release `
  -VersionLabel v1.0.0
```

Private repo 側の publish artifact を先に同期して使う場合:

```powershell
./scripts/sync_private_engine_worker_artifact.ps1
./scripts/create_github_release_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot artifacts/github-release `
  -VersionLabel v1.0.0 `
  -PreparedWorkerPublishDir artifacts/rescue-worker/publish/Release-win-x64
```

workflow preview で private publish run を固定して確認する場合:

```powershell
$env:GH_TOKEN = "..."
./scripts/invoke_github_release_preview.ps1 `
  -Ref workthree `
  -PrivateEngineRunId 23966594219 `
  -Wait
```

workflow preview で private release asset を固定して確認する場合:

```powershell
$env:GH_TOKEN = "..."
./scripts/invoke_github_release_preview.ps1 `
  -Ref workthree `
  -PrivateEngineReleaseTag v1.0.3.4-private.1 `
  -Wait
```

2026-04-04 の live 確認:

- private release asset preview run: `23979016211`
- private publish artifact preview run: `23978177837`

生成物:

- `artifacts/github-release/*.zip`
- `artifacts/github-release/package/*`
- `artifacts/github-release/publish/*`
- `artifacts/github-release/release-worker-lock-summary-*.md`
- app package 内に `rescue-worker\*`
- app package 内に `rescue-worker-expected.json`
- app package 内に `rescue-worker.lock.json`
- app package 内に `rescue-worker-lock-summary.txt`

rescue worker artifact を個別に作る場合:

```powershell
./scripts/sync_private_engine_worker_artifact.ps1 -ReleaseTag v1.0.0
./scripts/create_rescue_worker_artifact_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -VersionLabel v1.0.0 `
  -PreparedWorkerPublishDir artifacts/rescue-worker/publish/Release-win-x64
```

local worker source build を使う local 開発時だけの例外:

```powershell
./scripts/create_rescue_worker_artifact_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -VersionLabel v1.0.0 `
  -AllowLocalWorkerSourceBuild
```

生成物:

- `artifacts/rescue-worker/*.zip`
- `artifacts/rescue-worker/package/*`
- `artifacts/rescue-worker/publish/*`
- worker ZIP 名には `compat-<CompatibilityVersion>` が入る

workflow preview だけ GitHub 上で確認したい場合:

```powershell
$env:GH_TOKEN = '***'
./scripts/invoke_github_release_preview.ps1 -Ref workthree -Wait
```

補足:
- `GH_TOKEN` と `GITHUB_TOKEN` のどちらでもよい
- token には対象 repo へ `workflow_dispatch` を送れる権限が必要
- 成功時は preview run URL が出る

## 4. GitHub Release へ載せる手順

### 4.1 まずローカルで配布 ZIP を確認する

```powershell
./scripts/create_github_release_package.ps1 -Configuration Release -Runtime win-x64 -VersionLabel v1.0.0
```

確認ポイント:

- `IndigoMovieManager_fork_workthree.exe` が入っている
- `avcodec-61.dll` など `tools\ffmpeg-shared` 由来の DLL が入っている
- `Sinku.dll` と `.ini` 群が入っている
- `Images` 配下の必要ファイルが入っている
- `rescue-worker\IndigoMovieManager.Thumbnail.RescueWorker.exe` が入っている
- `rescue-worker\rescue-worker-artifact.json` が入っている
- `rescue-worker-expected.json` に同梱 worker の相対パスと compatibilityVersion が入っている
- `rescue-worker.lock.json` に同梱 worker の version / compatibilityVersion / sha256 が入っている
- `rescue-worker-lock-summary.txt` に同梱 worker の pin 情報要約が入っている
- `artifacts/github-release/release-worker-lock-summary-*.md` に package 外から見える pin 情報要約が出ている
- `artifacts/github-release/release-worker-lock-summary-*.md` には GitHub Release 本文へ貼る block と `package / lockFile` の確認情報が入っている

### 4.2 タグを切って push する

```powershell
git tag v1.0.0
git push origin v1.0.0
```

これで `.github/workflows/github-release-package.yml` が走り、GitHub Release へ app ZIP が添付される。tag release では private release asset から同期した worker pin 情報も本文先頭へ入る。

## 5. workflow の動き

1. Windows runner で checkout
2. .NET 8 SDK をセットアップ
3. `scripts/create_github_release_package.ps1` で app ZIP を作る
4. Actions Artifact へ app ZIP を保存
5. `release-worker-lock-summary-*.md` を Actions Artifact へ保存
6. workflow summary へ `Release Body Preview` を表示
7. タグ実行時だけ GitHub Release へ app ZIP を添付し、tag release では private release asset を正本同期元にする

補足:
- `create_github_release_package.ps1` の中で `verify_app_package_worker_lock.ps1` を呼び、lock / expected / marker / bundled worker の整合を事前確認する
- `workflow_dispatch` 実行時は `github-release-body-preview` artifact を見れば、Release 本文へ入る worker pin 情報を GitHub 上で先に確認できる
- 同じ内容は Actions の run summary にも出るので、artifact を開かなくてもざっと確認できる
- `invoke_github_release_preview.ps1 -Wait` を使えば、preview run URL までローカルから追える

## 6. 注意点

- 現在の既定は `--self-contained false` なので、配布先には `.NET 8 Desktop Runtime` が必要
- 依存 DLL をまとめて使うため、配布は必ず ZIP 単位で扱う
- `tools\ffmpeg\ffmpeg.exe` がローカルにある場合は publish 出力へ同梱される
- rescue worker artifact は `rescue-worker-artifact.json` の `compatibilityVersion` 一致が前提
- app package は `rescue-worker` フォルダへ worker を同梱し、`rescue-worker-expected.json` で相対パスと compatibilityVersion を明示する
- app package は `rescue-worker.lock.json` で同梱 worker の pin 情報も持つ
- app package は `rescue-worker-lock-summary.txt` で人間向けの pin 要約も持つ
- release helper は `artifacts/github-release/release-worker-lock-summary-*.md` で package 外にも pin 要約を残す
- release helper が出す summary markdown は、workflow の `body_path` 正本としても使われる
- `github-release-package.yml` は同じ summary markdown を `github-release-body-preview` artifact としても保存する
- GitHub Releases には app ZIP だけを載せる
- worker 単体の切り分けが必要な時は Private repo の `private-engine-publish` を手動実行する
- Release 名や本文を細かく制御したい場合は、workflow を追加調整する
