# Implementation Plan_release package_worker期待version固定_2026-03-17

## 1. 目的

- app 側 release package が、どの rescue worker artifact を期待しているかを配布物の中で明示する
- 同じ tag で出した app ZIP と worker ZIP の対応を、後から機械的に追えるようにする
- launcher 実装だけでなく、配布運用の面でも `compatibilityVersion` を固定する

## 2. 今回の反映

- `scripts/create_github_release_package.ps1`
  - `RescueWorkerArtifactContract.CompatibilityVersion` を読んで app package へ埋めるようにした
  - 想定 worker asset 名は `...-compat-<CompatibilityVersion>.zip` で固定した
  - `README-package.txt` に
    - 想定 rescue worker artifact ZIP 名
    - 想定 rescue worker compatibilityVersion
    を追記
  - `rescue-worker-expected.json` を package へ追加
  - `rescue-worker.lock.json` を package へ追加
  - `rescue-worker-lock-summary.txt` を package へ追加し、人間向け pin 情報も残すようにした
  - `artifacts/github-release/release-worker-lock-summary-<version>-<runtime>.md` も出し、GitHub Release 本文へ転記する markdown を package 外にも残すようにした
  - `verify_app_package_worker_lock.ps1` を呼び、lock / expected / marker / bundled worker の整合を smoke 確認するようにした
- `scripts/invoke_release.ps1`
  - package 作成後に worker lock を読み、console 表示に加えて同名の summary markdown も release 出力直下へ残すようにした
  - summary markdown には GitHub Release 本文へ転記する block も持たせた

## 3. 生成されるもの

app package 内に次が入る。

- `README-package.txt`
- `rescue-worker-expected.json`
- `rescue-worker.lock.json`
- `rescue-worker-lock-summary.txt`

release 出力直下には次も出る。

- `artifacts/github-release/release-worker-lock-summary-*.md`

`rescue-worker-expected.json` には次を持たせる。

- `artifactType`
- `versionLabel`
- `runtime`
- `bundledRescueWorkerRelativePath`
- `expectedRescueWorkerCompatibilityVersion`

`rescue-worker.lock.json` には、少なくとも次を持たせる。

- `schemaVersion`
- `workerArtifact.artifactType`
- `workerArtifact.sourceType`
- `workerArtifact.version`
- `workerArtifact.assetFileName`
- `workerArtifact.compatibilityVersion`
- `workerArtifact.workerExecutableSha256`

`rescue-worker-lock-summary.txt` には、少なくとも次を人間向けに書く。

- `source`
- `version`
- `asset`
- `compatibilityVersion`
- `workerExecutableSha256`
- `bundledWorker`

## 4. 今の意味

- 同じ `v*` tag で app workflow と worker workflow が走った時、asset 名だけで対応を追える
- app package 単体を見ても、期待する worker artifact が分かる
- 将来 release 管理を別 repo に寄せても、この manifest を見れば host 側期待値を引き継げる
- launcher 側は `rescue-worker.lock.json` を読んで、同梱 worker の `compatibilityVersion / sha256` を fail-fast で確認できる
- README と summary text を見れば、人間も app package 単体で worker pin を追える
- release helper の summary markdown を見れば、ZIP を開かなくても worker pin を追える
- summary markdown の block を見れば、Release 本文へ worker pin を手で転記しやすい

## 5. 残件

- GitHub Release 本文へ app/worker 対応情報を自動展開するところまでは未着手
- app package 作成時に、対応する worker ZIP が実在するかまで確認する仕組みはまだ無い
