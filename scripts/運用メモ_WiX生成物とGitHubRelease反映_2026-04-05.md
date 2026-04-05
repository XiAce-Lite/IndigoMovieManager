# 運用メモ_WiX生成物とGitHubRelease反映_2026-04-05

最終更新日: 2026-04-05

## 1. この文書の目的

この文書は、`WiX installer` 導線で

- どこに何が生成されるか
- どれが GitHub Actions artifact へ上がるか
- どれが GitHub Release asset へ上がるか

を、人が短時間で確認できるように整理した運用メモである。

前提:

- installer の唯一の入力は、`scripts/create_github_release_package.ps1` が作る verify 済み app package
- installer 側は worker / engine package を再 build しない
- provenance 正本は package 直下の `rescue-worker.lock.json` と `privateEnginePackages`

## 2. installer の入力になるもの

installer は、まず app package 側が先に生成されている必要がある。

正面入口:

- `scripts/create_github_release_package.ps1`

既定の生成先:

- publish 展開: `artifacts/github-release/publish/<VersionLabel>-win-x64/`
- verify 済み package: `artifacts/github-release/package/IndigoMovieManager-<VersionLabel>-win-x64/`
- 利用者向け ZIP: `artifacts/github-release/IndigoMovieManager-<VersionLabel>-win-x64.zip`
- Release 本文 preview: `artifacts/github-release/release-worker-lock-summary-<VersionLabel>-win-x64.md`

installer が直接使う入力:

- `artifacts/github-release/package/IndigoMovieManager-<VersionLabel>-win-x64/`

この package の中には少なくとも次が入っている前提で動く。

- `IndigoMovieManager.exe`
- `rescue-worker.lock.json`
- `rescue-worker/`
- `tools/ffmpeg-shared/`
- `skin/`

## 3. local で WiX を回した時の生成先

正面入口:

- `scripts/create_wix_installer_from_release_package.ps1`

既定の出力 root:

- `artifacts/github-release/installer/`

`-VersionLabel v1.0.3.5 -Runtime win-x64` の例では、次が生成される。

### 3.1 中間生成物

- `artifacts/github-release/installer/v1.0.3.5-win-x64/msi/IndigoMovieManager.msi`
- `artifacts/github-release/installer/v1.0.3.5-win-x64/msi/IndigoMovieManager.wixpdb`
- `artifacts/github-release/installer/v1.0.3.5-win-x64/bundle/IndigoMovieManager.Bundle.exe`
- `artifacts/github-release/installer/v1.0.3.5-win-x64/bundle/IndigoMovieManager.Bundle.wixpdb`

意味:

- `MSI` は bundle の内部 package
- `Bundle.exe` は WiX の生出力
- `*.wixpdb` はデバッグ・調査用

### 3.2 最終生成物

- `artifacts/github-release/installer/IndigoMovieManager-Setup-v1.0.3.5-win-x64.exe`

意味:

- これが利用者向けの installer 正面入口
- GitHub Release へ載せる対象もこの `Setup-*.exe`

### 3.3 2026-04-05 の local proof

PR 前確認として、次で local proof 成功を確認済み。

- 入力 package:
  - `artifacts/github-release/package/IndigoMovieManager-v1.0.3.5-win-x64/`
- 出力 root:
  - `artifacts/github-release/pre-pr-wix-check/`

この時の生成物:

- `artifacts/github-release/pre-pr-wix-check/pre-pr-wix-check-win-x64/msi/IndigoMovieManager.msi`
- `artifacts/github-release/pre-pr-wix-check/pre-pr-wix-check-win-x64/bundle/IndigoMovieManager.Bundle.exe`
- `artifacts/github-release/pre-pr-wix-check/IndigoMovieManager-Setup-pre-pr-wix-check-win-x64.exe`

## 4. GitHub Actions で何が上がるか

対象 workflow:

- `.github/workflows/github-release-package.yml`

この workflow は、

1. Private engine の worker / package を同期
2. verify 済み app package を生成
3. その package を入力に WiX installer を生成
4. artifact を upload
5. tag 実行時だけ GitHub Release へ publish

の順で動く。

### 4.1 workflow_dispatch / preview 時

artifact 名は次の 3 つ。

- `github-release-package`
  - 中身: `artifacts/github-release/*.zip`
  - 例: `IndigoMovieManager-manual-1234-win-x64.zip`
- `github-release-installer`
  - 中身: `artifacts/github-release/installer/*.exe`
  - 例: `IndigoMovieManager-Setup-manual-1234-win-x64.exe`
- `github-release-body-preview`
  - 中身: `artifacts/github-release/release-worker-lock-summary-*.md`

重要:

- preview では `MSI` は upload しない
- preview では `bundle/IndigoMovieManager.Bundle.exe` も直接は upload しない
- preview では最終 `Setup-*.exe` だけが installer artifact へ入る
- `publish/` や `package/` ディレクトリそのものも upload しない

### 4.2 tag push / Release 時

tag 実行では、上の artifact upload に加えて `softprops/action-gh-release@v2` が動く。

GitHub Release asset へ載るもの:

- `artifacts/github-release/*.zip`
  - 例: `IndigoMovieManager-v1.0.3.5-win-x64.zip`
- `artifacts/github-release/installer/*.exe`
  - 例: `IndigoMovieManager-Setup-v1.0.3.5-win-x64.exe`

GitHub Release asset へ載らないもの:

- `artifacts/github-release/package/...`
- `artifacts/github-release/publish/...`
- `artifacts/github-release/installer/<VersionLabel>-win-x64/msi/IndigoMovieManager.msi`
- `artifacts/github-release/installer/<VersionLabel>-win-x64/bundle/IndigoMovieManager.Bundle.exe`
- `*.wixpdb`
- `release-worker-lock-summary-*.md`

補足:

- `release-worker-lock-summary-*.md` は Release 本文の元データとして使う
- ただし asset としては公開しない

## 5. 生成物と公開先の対応表

| 生成物 | 生成場所 | 作る script / step | Actions artifact | GitHub Release asset |
| --- | --- | --- | --- | --- |
| verify 済み app package | `artifacts/github-release/package/IndigoMovieManager-<VersionLabel>-win-x64/` | `scripts/create_github_release_package.ps1` | いいえ | いいえ |
| app ZIP | `artifacts/github-release/IndigoMovieManager-<VersionLabel>-win-x64.zip` | `scripts/create_github_release_package.ps1` | はい `github-release-package` | tag 時のみ はい |
| Release 本文 preview | `artifacts/github-release/release-worker-lock-summary-<VersionLabel>-win-x64.md` | `scripts/create_github_release_package.ps1` | はい `github-release-body-preview` | いいえ |
| MSI | `artifacts/github-release/installer/<VersionLabel>-win-x64/msi/IndigoMovieManager.msi` | `scripts/create_wix_installer_from_release_package.ps1` | いいえ | いいえ |
| bundle 生出力 | `artifacts/github-release/installer/<VersionLabel>-win-x64/bundle/IndigoMovieManager.Bundle.exe` | `scripts/create_wix_installer_from_release_package.ps1` | いいえ | いいえ |
| 最終 installer | `artifacts/github-release/installer/IndigoMovieManager-Setup-<VersionLabel>-win-x64.exe` | `scripts/create_wix_installer_from_release_package.ps1` | はい `github-release-installer` | tag 時のみ はい |
| `*.wixpdb` | `artifacts/github-release/installer/<VersionLabel>-win-x64/...` | WiX build | いいえ | いいえ |

## 6. どれを見ればよいか

### 6.1 local 確認

- package 内容を見たい:
  - `artifacts/github-release/package/IndigoMovieManager-<VersionLabel>-win-x64/`
- installer 完成物を見たい:
  - `artifacts/github-release/installer/IndigoMovieManager-Setup-<VersionLabel>-win-x64.exe`
- MSI や bundle の中間物を見たい:
  - `artifacts/github-release/installer/<VersionLabel>-win-x64/`

### 6.2 GitHub Actions 確認

- ZIP を見たい:
  - artifact `github-release-package`
- installer を見たい:
  - artifact `github-release-installer`
- Release 本文 preview を見たい:
  - artifact `github-release-body-preview`

### 6.3 GitHub Release 確認

tag release 後に見るべきもの:

- `IndigoMovieManager-<Version>-win-x64.zip`
- `IndigoMovieManager-Setup-<Version>-win-x64.exe`

見えなくて正常なもの:

- `IndigoMovieManager.msi`
- `IndigoMovieManager.Bundle.exe`
- `*.wixpdb`
- `release-worker-lock-summary-*.md`

## 7. 実務判断

- 利用者へ配る正面入口は `Setup-*.exe`
- fallback / 手動展開の正面入口は `ZIP`
- `MSI` は内部構成要素として扱い、公開 asset にしない
- installer 側の provenance は新しく作らず、package 直下の `rescue-worker.lock.json` と `privateEnginePackages` をそのまま正本にする

この前提を崩す時は、installer 単体ではなく release 本線全体の見直しとして扱う。
