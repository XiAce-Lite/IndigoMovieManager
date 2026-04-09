---
name: indigomoviemanager-github-release
description: IndigoMovieManager で GitHub Release の preview を回したい、tag を push して公開したい、github-release-package.yml の失敗を復旧したい時に使う。特に private_engine_release_tag と private_engine_run_id の切り分け、公開ミラーと private repo の責務整理、tag 実行時の PRIVATE_ENGINE_RELEASE_TAG 救済判断が必要なケースに使う。
---

# IndigoMovieManager GitHub Release

## 目的

`IndigoMovieManager_fork` の preview 実行と release 公開を、安全な順序で進める。
失敗時は `release_tag` 経路と `run_id` 経路を切り分け、公開ミラーと private repo を混同せずに復旧する。

## まず読む正本

- 通常運用: `Docs/forHuman/GitHubRelease/AI向け_公開手順書_公開ミラーGitHubRelease運用_2026-04-06.md`
- 復旧判断: `Docs/forHuman/GitHubRelease/AI向け_公開ミラーGitHubRelease復旧手順_run_id_release_tag_2026-04-06.md`
- workflow: `.github/workflows/github-release-package.yml`

通常は通常運用を正本にし、失敗調査や rerun 判断が必要な時だけ復旧手順書も開く。

## 最重要ルール

- `release_tag` は GitHub Release asset を探す経路。
- `run_id` は GitHub Actions artifact を探す経路。
- 公開ミラー repo `T-Hamada0101/IndigoMovieEngine-Mirror` は release asset の入口であり、`run_id` の正本ではない。
- `run_id` の正本は private repo `T-Hamada0101/IndigoMovieEngine`。
- 入力なし `workflow_dispatch` は `PRIVATE_ENGINE_PUBLISH_RUN_ID` を preview fallback として使う。
- tag push で `github-release-package.yml` が走り、最終的に GitHub Release を公開する。
- app tag と engine release tag がズレる tag 実行では、必要なら repo variable `PRIVATE_ENGINE_RELEASE_TAG` を一時的に使って救済し、成功後に削除する。

## 実行前チェック

1. `gh auth status` で GitHub CLI 認証を確認する。
2. 作業場所を `%USERPROFILE%\\source\\repos\\IndigoMovieManager` に合わせる。
3. 公開したい commit が `origin` に push 済みか確認する。
4. 原則として tag push 前に preview を 1 回成功させる。

## 標準フロー

### 1. 最初は入力なし preview

何も指定せず `workflow_dispatch` を実行する。
この時は `PRIVATE_ENGINE_PUBLISH_RUN_ID` が fallback として使われる。

```powershell
Set-Location "$env:USERPROFILE\source\repos\IndigoMovieManager"
gh workflow run github-release-package.yml -R T-Hamada0101/IndigoMovieManager_fork
gh run list -R T-Hamada0101/IndigoMovieManager_fork --workflow github-release-package.yml --limit 5
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
```

### 2. release asset を明示したい時は release_tag

公開ミラー release と private release を順に見せたい時は `private_engine_release_tag` を使う。

```powershell
Set-Location "$env:USERPROFILE\source\repos\IndigoMovieManager"
gh workflow run github-release-package.yml -R T-Hamada0101/IndigoMovieManager_fork -f private_engine_release_tag=v1.0.3.5
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
```

### 3. release asset が不足する時は run_id

worker と packages の両 artifact を持つ private run だけを使う。
迷ったら `23997659256` を基準にする。

```powershell
Set-Location "$env:USERPROFILE\source\repos\IndigoMovieManager"
gh workflow run github-release-package.yml -R T-Hamada0101/IndigoMovieManager_fork -f private_engine_run_id=23997659256
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
```

### 4. 公開は tag push

preview 成功後に branch を push し、release tag を push する。

```powershell
Set-Location "$env:USERPROFILE\source\repos\IndigoMovieManager"
git push origin master
git tag -a "vX.Y.Z" -m "Release vX.Y.Z"
git push origin "vX.Y.Z"
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
gh release view vX.Y.Z -R T-Hamada0101/IndigoMovieManager_fork --json tagName,name,url,isDraft,isPrerelease,assets
```

## tag 実行の救済

tag push 時は workflow input を直接渡せない。
app tag と engine release tag がズレる時は、必要なら `PRIVATE_ENGINE_RELEASE_TAG` を一時設定して rerun する。

```powershell
Set-Location "$env:USERPROFILE\source\repos\IndigoMovieManager"
gh variable set PRIVATE_ENGINE_RELEASE_TAG -R T-Hamada0101/IndigoMovieManager_fork --body "<engine-release-tag>"
gh run rerun <failed-run-id> -R T-Hamada0101/IndigoMovieManager_fork
gh run watch <run-id> -R T-Hamada0101/IndigoMovieManager_fork --exit-status
gh release view <app-tag> -R T-Hamada0101/IndigoMovieManager_fork --json tagName,name,url,assets
gh variable delete PRIVATE_ENGINE_RELEASE_TAG -R T-Hamada0101/IndigoMovieManager_fork
```

修正 commit を含めたい時は rerun より新しい tag を切り直す方を優先する。

## 失敗時の切り分け順

1. workflow log で `private engine source mode` を確認する。
2. `release_tag` 経路か `run_id` 経路かを確定する。
3. `run_id` 経路なら private repo の run を見ているか確認する。
4. `run_id` 経路なら、その run に `rescue-worker-publish` と `private-engine-packages` の両 artifact があるか確認する。
5. `release_tag` 経路なら、公開ミラー release に worker zip と必要 package が揃っているか確認する。
6. 公開ミラーで不足するなら private release を確認する。
7. それでも不足するなら `private_engine_run_id` を明示して preview をやり直す。

復旧が長引く時は復旧手順書を開いて、過去の失敗パターンと既知の NG run_id を先に確認する。

## よく使う確認コマンド

```powershell
gh run list -R T-Hamada0101/IndigoMovieManager_fork --workflow github-release-package.yml --limit 10
gh run view <run-id> -R T-Hamada0101/IndigoMovieManager_fork --json status,conclusion,jobs,url
gh run view <run-id> -R T-Hamada0101/IndigoMovieManager_fork --log-failed
gh release view <tag> -R T-Hamada0101/IndigoMovieManager_fork --json tagName,name,url,isDraft,isPrerelease,assets
gh variable list -R T-Hamada0101/IndigoMovieManager_fork
```

## やってはいけないこと

- 公開ミラー release が見えるだけで `run_id` も使えると判断しない。
- worker だけある run を `PRIVATE_ENGINE_PUBLISH_RUN_ID` に設定しない。
- `PRIVATE_ENGINE_RELEASE_TAG` を救済後に残置しない。
- 失敗ログを見ずに workflow 全体を書き換え始めない。

## 成功条件

1. `github-release-package.yml` が success で終わる。
2. GitHub Release に `zip` と installer `exe` が揃う。
3. 一時 variable を使った場合は削除済みである。
