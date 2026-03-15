# GitHub Release 実行可能バイナリ配布手順 2026-03-15

最終更新日: 2026-03-15

## 1. 方針

- 実行可能バイナリは Git の履歴へ直接 commit しない
- 配布物は GitHub の Release へ ZIP を添付する
- `exe` 単体ではなく、publish 出力一式を ZIP 化して配布する

## 2. このリポジトリで追加したもの

- `scripts/create_github_release_package.ps1`
  - PowerShell 7 前提の配布 ZIP 生成スクリプト
- `.github/workflows/github-release-package.yml`
  - `v*` タグ push で ZIP を GitHub Release へ添付する workflow

## 3. ローカルで ZIP を作る手順

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

### 4.2 タグを切って push する

```powershell
git tag v1.0.0
git push origin v1.0.0
```

これで `.github/workflows/github-release-package.yml` が走り、GitHub Release へ ZIP が添付される。

## 5. workflow の動き

1. Windows runner で checkout
2. .NET 8 SDK をセットアップ
3. `scripts/create_github_release_package.ps1` で publish と ZIP 化
4. Actions Artifact へ ZIP を保存
5. タグ実行時だけ GitHub Release へ ZIP を添付

## 6. 注意点

- 現在の既定は `--self-contained false` なので、配布先には `.NET 8 Desktop Runtime` が必要
- 依存 DLL をまとめて使うため、配布は必ず ZIP 単位で扱う
- `tools\ffmpeg\ffmpeg.exe` がローカルにある場合は publish 出力へ同梱される
- Release 名や本文を細かく制御したい場合は、workflow を追加調整する
