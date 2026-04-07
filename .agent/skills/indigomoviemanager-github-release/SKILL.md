---
name: indigomoviemanager-github-release
description: IndigoMovieManager で GitHub Release したい、release まで進めたい、tag を作って公開したい、workflow_dispatch で preview したい、release 失敗を復旧したい時に使用する。特に github-release-package.yml、private_engine_release_tag、private_engine_run_id、PUBLIC_ENGINE_MIRROR_REPO、PRIVATE_ENGINE_PUBLISH_RUN_ID、PRIVATE_ENGINE_RELEASE_TAG の判断が必要なケースに使う。
---

# IndigoMovieManager GitHub Release

## 目的

IndigoMovieManager_fork の release package zip と WiX installer exe を安全に作成し、必要なら tag 実行で GitHub Release へ公開する。
失敗時は release_tag 経路と run_id 経路を切り分け、公開ミラーと private repo の責務を混同せずに復旧する。

## 正本ファイル

- workflow: .github/workflows/github-release-package.yml
- 通常運用: Docs/forHuman/GitHubRelease/AI向け_公開手順書_公開ミラーGitHubRelease運用_2026-04-06.md
- 復旧判断: Docs/forHuman/GitHubRelease/AI向け_公開ミラーGitHubRelease復旧手順_run_id_release_tag_2026-04-06.md
- package version fallback: Directory.Build.props
- release package script: scripts/create_github_release_package.ps1

## まず覚えること

- release_tag は GitHub Release asset を探す経路。
- run_id は GitHub Actions artifact を探す経路。
- 公開ミラー T-Hamada0101/IndigoMovieEngine-Mirror は release asset の入口であり、run_id の正本ではない。
- run_id の正本は private repo T-Hamada0101/IndigoMovieEngine。
- app の tag 名と engine の release tag 名は同一とは限らない。
- tag 実行時の private engine source 解決順は次の通り。

1. workflow input private_engine_release_tag
2. repository variable PRIVATE_ENGINE_RELEASE_TAG
3. 現在の app tag 名

このため、app tag と engine release tag がズレるケースでは、一時的に PRIVATE_ENGINE_RELEASE_TAG を使って tag 実行を救済できる。

## 2026-04-07 時点の既知の正値

- Public app repo: T-Hamada0101/IndigoMovieManager_fork
- Public mirror repo: T-Hamada0101/IndigoMovieEngine-Mirror
- Private engine repo: T-Hamada0101/IndigoMovieEngine
- Repository Variable: PUBLIC_ENGINE_MIRROR_REPO = T-Hamada0101/IndigoMovieEngine-Mirror
- Repository Variable: PRIVATE_ENGINE_PUBLISH_RUN_ID = 23997659256
- app v1.0.3.5-r3 を成功させた engine release tag: v1.0.3.5-private.2

## 実行前チェック

1. gh auth status で GitHub CLI 認証済みを確認する。
2. 作業ディレクトリを repo root に合わせる。
3. 対象 commit が remote へ push 済みか確認する。
4. tag 公開前に、原則として workflow_dispatch preview を先に成功させる。
5. 失敗調査を始める前に、workflow の source mode が release_tag か run_id かをログで確認する。

## 標準運用

### A. preview を最短で確認する

最初に選ぶ既定手順。入力なし workflow_dispatch を使う。
この経路では PRIVATE_ENGINE_PUBLISH_RUN_ID が preview fallback として使われる。

```powershell
Set-Location "C:\Users\na6ce\source\repos\IndigoMovieManager"
gh workflow run github-release-package.yml -R T-Hamada0101/IndigoMovieManager_fork
gh run list -R T-Hamada0101/IndigoMovieManager_fork --workflow github-release-package.yml --limit 5
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
```

### B. 特定の engine release tag で preview する

公開ミラー release と private release を順に見せたい時に使う。

```powershell
Set-Location "C:\Users\na6ce\source\repos\IndigoMovieManager"
gh workflow run github-release-package.yml -R T-Hamada0101/IndigoMovieManager_fork -f private_engine_release_tag=v1.0.3.5-private.2
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
```

### C. 特定の private run_id で preview する

release asset が不足している時や、artifact 正本を直接参照したい時に使う。
worker と packages の両 artifact を持つ private run だけを指定する。

```powershell
Set-Location "C:\Users\na6ce\source\repos\IndigoMovieManager"
gh workflow run github-release-package.yml -R T-Hamada0101/IndigoMovieManager_fork -f private_engine_run_id=23997659256
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
```

### D. 実際に GitHub Release を公開する

公開は tag push で行う。preview 成功後に実施する。

```powershell
Set-Location "C:\Users\na6ce\source\repos\IndigoMovieManager"
git push origin master
git tag -a "v1.0.3.5-r4" -m "Release v1.0.3.5-r4"
git push origin "v1.0.3.5-r4"
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
gh release view v1.0.3.5-r4 -R T-Hamada0101/IndigoMovieManager_fork --json tagName,name,url,isDraft,isPrerelease,assets
```

## tag 実行の救済手順

tag 実行では workflow input を直接渡せないため、app tag 名と engine release tag 名がズレる時は repository variable を一時的に使う。

### 使う場面

- app tag は v1.0.3.5-r3
- engine release tag は v1.0.3.5-private.2
- workflow が app tag 名を engine release tag と誤認して 404 や asset 不足で失敗する

### 手順

1. 正しい engine release tag を確認する。
2. 一時的に PRIVATE_ENGINE_RELEASE_TAG を設定する。
3. 失敗済み tag run を rerun するか、新しい app tag を作る。
4. 成功後に Release と asset を確認する。
5. PRIVATE_ENGINE_RELEASE_TAG を必ず削除する。

```powershell
Set-Location "C:\Users\na6ce\source\repos\IndigoMovieManager"
gh variable set PRIVATE_ENGINE_RELEASE_TAG -R T-Hamada0101/IndigoMovieManager_fork --body "v1.0.3.5-private.2"
gh run rerun <failed-run-id> -R T-Hamada0101/IndigoMovieManager_fork
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
gh release view <app-tag> -R T-Hamada0101/IndigoMovieManager_fork --json tagName,name,url,assets
gh variable delete PRIVATE_ENGINE_RELEASE_TAG -R T-Hamada0101/IndigoMovieManager_fork
gh variable list -R T-Hamada0101/IndigoMovieManager_fork
```

新しい修正 commit を含めたい場合は、古い run を rerun するより新しい app tag を作る方が安全である。

## 失敗時の切り分け順

1. workflow log で private engine source mode を確認する。
2. release_tag 経路か run_id 経路かを確定する。
3. run_id 経路なら private repo を見ているか確認する。
4. run_id 経路なら、その private run に rescue-worker-publish と private-engine-packages の両 artifact があるか確認する。
5. release_tag 経路なら、公開ミラー release に worker zip と nupkg 3 点が揃っているか確認する。
6. 公開ミラーで不足するなら private release を確認する。
7. release_tag でも不足するなら private_engine_run_id を明示して workflow_dispatch を再実行する。
8. package 作成段階で失敗したら scripts/create_github_release_package.ps1 を確認する。
9. restore 段階で package version 未設定が出たら Directory.Build.props の manifest fallback を確認する。

## よくある失敗と対処

### 1. tag 実行で engine release が見つからない

症状例:

```text
release asset が見つからない
404 Not Found
```

原因:

- app tag 名を engine release tag 名として扱っている。

対処:

- 正しい engine release tag を確認する。
- 一時的に PRIVATE_ENGINE_RELEASE_TAG を設定する。
- 修正 commit を含めたい時は新しい app tag を作り直す。

### 2. package nupkg が見つからない

症状例:

```text
release asset が一意に見つかりません
```

原因:

- 公開ミラー release に package asset が揃っていない。

対処:

- private_engine_release_tag で private release 側を試す。
- それでも不足するなら private_engine_run_id を指定する。

### 3. restore 時に package version 未設定で落ちる

症状例:

```text
ImmUsePrivateEnginePackages=true ですが ImmThumbnailContractsPackageVersion が未設定です。
```

対処:

- Directory.Build.props が artifacts/private-engine-packages/Release/private-engine-packages-manifest.json を読めるか確認する。
- local sync 済み package dir が存在するか確認する。

### 4. Create release package で metadata property missing が出る

症状例:

```text
The property 'manifestFileName' cannot be found on this object.
```

対処:

- scripts/create_github_release_package.ps1 の optional property handling を確認する。
- 次の property は省略される前提で扱う。
  - manifestFileName
  - assetFileName
  - sourceArtifactName
  - compatibilityVersion

## 実運用で参照するコマンド

```powershell
gh run list -R T-Hamada0101/IndigoMovieManager_fork --workflow github-release-package.yml --limit 10
gh run view <run-id> -R T-Hamada0101/IndigoMovieManager_fork --json status,conclusion,jobs,url
gh run view <run-id> -R T-Hamada0101/IndigoMovieManager_fork --log-failed
gh release view <tag> -R T-Hamada0101/IndigoMovieManager_fork --json tagName,name,url,isDraft,isPrerelease,assets
gh variable list -R T-Hamada0101/IndigoMovieManager_fork
```

## 触るべきコード位置

- .github/workflows/github-release-package.yml
  - source mode と fallback 順
- Directory.Build.props
  - package version/source の local manifest fallback
- scripts/create_github_release_package.ps1
  - prepared worker metadata
  - prepared package metadata

## やってはいけないこと

- mirror repo の release が見えるだけで run_id も使えると判断しない。
- worker だけある run を PRIVATE_ENGINE_PUBLISH_RUN_ID に設定しない。
- tag 実行救済のために入れた PRIVATE_ENGINE_RELEASE_TAG を残置しない。
- release_tag と run_id を同じ意味として扱わない。
- 失敗ログを見ずに workflow 全体を書き換え始めない。

## 成功条件

1. github-release-package workflow が success で終了する。
2. GitHub Release が draft ではなく作成される。
3. 次の 2 asset が揃う。
   - IndigoMovieManager-<version>-win-x64.zip
   - IndigoMovieManager-Setup-<version>-win-x64.exe
4. 一時 variable を使った場合は削除済みである。
