# GitHub Release 実行可能バイナリ配布手順 2026-03-15

最終更新日: 2026-03-15

## 1. 方針

- 実行可能バイナリは Git の履歴へ直接 commit しない
- 配布物は GitHub の Release へ ZIP を添付する
- `exe` 単体ではなく、publish 出力一式を ZIP 化して配布する

## 2. このリポジトリで追加したもの

- `scripts/create_github_release_package.ps1`
  - PowerShell 7 前提の配布 ZIP 生成スクリプト
- `scripts/create_rescue_worker_artifact_package.ps1`
  - rescue worker artifact 用 ZIP 生成スクリプト
- `scripts/invoke_release.ps1`
  - clean worktree 前提で version 更新から tag push までを束ねる release helper
- `.github/workflows/github-release-package.yml`
  - `v*` タグ push で ZIP を GitHub Release へ添付する workflow
- `.github/workflows/rescue-worker-artifact.yml`
  - `v*` タグ push で rescue worker artifact ZIP を Actions Artifact として確認用に生成する workflow

## 3. ローカルで ZIP を作る手順

正式 release を最短で進めたい場合は、まず次も選べる。

```powershell
./scripts/invoke_release.ps1 -Version 1.0.0.0
```

この helper は、clean worktree 前提で
- version 更新
- Release build
- app / worker package 作成
- commit
- branch push
- tag 作成
- tag push
まで進める。

補足:
- `-AllowDirty` を使う時でも staged 変更は空であることが必要
- branch push と tag push を両方行う時は atomic push を使う

PowerShell 7 でリポジトリ直下へ移動して実行する。

```powershell
./scripts/create_github_release_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot artifacts/github-release `
  -VersionLabel v1.0.0
```

生成物:

- `artifacts/github-release/*.zip`
- `artifacts/github-release/package/*`
- `artifacts/github-release/publish/*`
- app package 内に `rescue-worker\*`
- app package 内に `rescue-worker-expected.json`

rescue worker artifact を個別に作る場合:

```powershell
./scripts/create_rescue_worker_artifact_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -VersionLabel v1.0.0
```

生成物:

- `artifacts/rescue-worker/*.zip`
- `artifacts/rescue-worker/package/*`
- `artifacts/rescue-worker/publish/*`
- worker ZIP 名には `compat-<CompatibilityVersion>` が入る

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

### 4.2 タグを切って push する

```powershell
git tag v1.0.0
git push origin v1.0.0
```

これで `.github/workflows/github-release-package.yml` が走り、GitHub Release へ同梱版 ZIP が添付される。

## 5. workflow の動き

1. Windows runner で checkout
2. .NET 8 SDK をセットアップ
3. `scripts/create_github_release_package.ps1` で publish と ZIP 化
4. Actions Artifact へ ZIP を保存
5. タグ実行時だけ GitHub Release へ同梱版 ZIP を添付

## 6. 注意点

- 現在の既定は `--self-contained false` なので、配布先には `.NET 8 Desktop Runtime` が必要
- 依存 DLL をまとめて使うため、配布は必ず ZIP 単位で扱う
- `tools\ffmpeg\ffmpeg.exe` がローカルにある場合は publish 出力へ同梱される
- rescue worker artifact は `rescue-worker-artifact.json` の `compatibilityVersion` 一致が前提
- app package は `rescue-worker` フォルダへ worker を同梱し、`rescue-worker-expected.json` で相対パスと compatibilityVersion を明示する
- GitHub Releases には同梱版だけを載せ、個別 worker ZIP は Actions Artifact 側で扱う
- Release 名や本文を細かく制御したい場合は、workflow を追加調整する
