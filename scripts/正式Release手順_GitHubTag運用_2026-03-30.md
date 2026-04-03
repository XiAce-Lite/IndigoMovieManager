# 正式Release手順 GitHubTag運用 2026-03-30

最終更新日: 2026-04-03

変更概要:
- `scripts` 配下の ps1 を起点にした正式 release 手順を整理
- `配布 ZIP 作成` と `正式 release 完了` の違いを明記
- tag push 後に GitHub 側で確認すべき点を固定
- `invoke_release.ps1` 追加後の最短経路を反映
- 利用者向けに Release asset は app ZIP のみに絞る運用へ更新

## 1. この資料の目的

- `scripts\create_github_release_package.ps1` だけで何が足りるかを明確にする
- バージョン更新から tag push、GitHub Release 確認までを 1 本の手順で辿れるようにする
- app package と rescue worker artifact の扱いを混同しないようにする

## 2. 現在の結論

- `scripts\create_github_release_package.ps1` は、app の配布 ZIP 作成としては十分に近い
- ただし正式 release 完了には、version 更新、commit / push、`v*` tag push、GitHub Actions 確認が別で必要
- `scripts\create_rescue_worker_artifact_package.ps1` は worker 単体 ZIP 作成用であり、app release の代わりではない
- 現在は `scripts\invoke_release.ps1` で、clean worktree 前提なら version 更新から tag push まで 1 指示で進められる
- `invoke_release.ps1` の既定は app release 優先で、worker 単体 ZIP は明示指定時だけローカル生成する
- `invoke_release.ps1` は worker lock の pin 情報を console 表示し、GitHub Release 本文へ貼りやすい summary markdown も release 出力直下へ残す
- tag push 後の GitHub Actions は、その summary markdown を `body_path` で読み、Release 本文先頭へ自動反映する

## 3. 関連ファイル

- `IndigoMovieManager.csproj`
  - app の version 定義
- `scripts/create_github_release_package.ps1`
  - app package を publish して ZIP 化する
- `scripts/create_rescue_worker_artifact_package.ps1`
  - rescue worker artifact を ZIP 化する
- `scripts/invoke_release.ps1`
  - version 更新、Release build、package 作成、commit、push、tag push を束ねる
- `scripts/invoke_github_release_preview.ps1`
  - token 環境変数だけで `github-release-package.yml` を `workflow_dispatch` 実行し、preview run URL まで追える
- `.github/workflows/github-release-package.yml`
  - `v*` tag push で app ZIP を GitHub Release へ添付する正本 workflow
  - `release-worker-lock-summary-*.md` を `body_path` として読み、worker pin 情報を Release 本文へ自動反映する
  - `workflow_dispatch` でも `github-release-body-preview` artifact を残し、GitHub 上で本文 preview を確認できる
  - Actions の run summary にも `Release Body Preview` を出す
- `.github/workflows/rescue-worker-artifact.yml`
  - `workflow_dispatch` 専用で worker ZIP を単体確認する補助 workflow

## 4. release 前に決めること

- 次の version
- tag 名
- release に入れる commit 範囲
- 必須の確認項目

この repo では tag 名は `v1.0.3.2` のように `v` 付きで揃える。

## 5. version 更新

更新先:
- `IndigoMovieManager.csproj`

更新する項目:
- `<Version>`
- `<FileVersion>`
- `<AssemblyVersion>`

この 3 つを同じ値へ揃える。

## 6. 最短の 1 指示 release

clean worktree で、そのまま正式 release まで進めたい時は次を使う。

```powershell
./scripts/invoke_release.ps1 -Version 1.0.3.2
```

この helper が行うこと:
- `IndigoMovieManager.csproj` の version 更新
- `dotnet msbuild` による Release build
- app package 作成
- commit
- branch push
- `v1.0.3.2` tag 作成
- tag push

安全側の制約:
- 既定では clean worktree 必須
- `-AllowDirty` を使う時でも staged 変更は空であることが必要
- `-AllowDirty` を使う時でも `IndigoMovieManager.csproj` 自体に既存差分があると停止
- 同じ tag が local / remote にあると停止
- detached HEAD では停止
- branch push と tag push を両方行う時は atomic push を使い、remote 側の半端状態を避ける

手順確認だけしたい時:

```powershell
./scripts/invoke_release.ps1 -Version 1.0.3.2 -DryRun -AllowDirty
```

worker 単体 ZIP もローカルで同時生成したい時:

```powershell
./scripts/invoke_release.ps1 -Version 1.0.3.2 -IncludeWorkerArtifactPackage
```

## 7. ローカル確認

最低限ここまではローカルで確認する。

```powershell
dotnet msbuild IndigoMovieManager.sln /p:Configuration=Release /p:Platform=x64
```

必要なテストがある場合は、対象テストも回す。

配布物の形まで確認したい時は、PowerShell 7 で次を実行する。

```powershell
./scripts/create_github_release_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot artifacts/github-release `
  -VersionLabel v1.0.3.2
```

必要なら worker 単体も作る。

```powershell
./scripts/create_rescue_worker_artifact_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -OutputRoot artifacts/rescue-worker `
  -VersionLabel v1.0.3.2
```

## 8. ローカルで見るべきもの

app package 側:
- `artifacts/github-release/*.zip`
- `artifacts/github-release/package/*`
- `rescue-worker/IndigoMovieManager.Thumbnail.RescueWorker.exe`
- `rescue-worker-expected.json`
- `rescue-worker.lock.json`
- `README-package.txt`
- `artifacts/github-release/release-worker-lock-summary-*.md`

補足:
- app package 生成時は `verify_app_package_worker_lock.ps1` で、`lock / expected / marker / bundled worker` の整合を先に確認する
- `invoke_release.ps1` は package 作成後に `rescue-worker.lock.json` を読み、`source / version / asset / compatibilityVersion / sha256` を表示する
- `create_github_release_package.ps1` は `artifacts/github-release/release-worker-lock-summary-<version>-<runtime>.md` を書き出す
- `invoke_release.ps1` はその pin 情報を console にも出す
- この markdown は `GitHub Release 本文へ貼るブロック` と `Package / LockFile` の確認情報を持ち、workflow がそのまま読む前提にしている
- tag release では `github-release-package.yml` がこの markdown を `body_path` として読み、GitHub 自動生成 notes の前に worker pin 情報を載せる
- つまり summary は「本文テンプレ」でもあり、「workflow が読む release body 素材」でもある

worker package 側:
- `artifacts/rescue-worker/*.zip`
- `artifacts/rescue-worker/package/*`
- `rescue-worker-artifact.json`
- `README-artifact.txt`

GitHub Actions preview 側:
- `github-release-body-preview` artifact
- 中身は `release-worker-lock-summary-*.md`
- `workflow_dispatch` で回せば、本番 tag を切る前に GitHub 上で本文 preview を確認できる
- 同じ内容は run summary にも出るので、artifact download なしでも見られる
- `invoke_github_release_preview.ps1 -Wait` を使えば preview run URL をローカルから追える

## 9. commit / push

version 更新や release 用の doc 更新を含めて commit し、通常 push する。

```powershell
git push origin HEAD
```

注意:
- branch push だけでは GitHub Release は作られない

## 10. tag 作成と push

正式 release の本体はここである。

```powershell
git tag v1.0.3.2
git push origin v1.0.3.2
```

これで次が走る。

- `.github/workflows/github-release-package.yml`

## 11. GitHub 側で確認すること

### 10.1 app release

- `github-release-package` workflow が成功している
- GitHub Release が作られている
- app ZIP が Release asset に添付されている
- Release 本文の先頭に `Bundled Rescue Worker` ブロックが入っている

### 10.1.1 preview run

- `workflow_dispatch` 実行時は `github-release-body-preview` artifact が取れる
- その markdown が GitHub Release 本文へ入る想定内容と一致している
- run summary にも同じ `Release Body Preview` が出ている

### 10.2 worker artifact

- worker 単体確認が必要な時だけ `rescue-worker-artifact` を手動実行する
- 実行時は worker ZIP が Actions Artifact にある

補足:
- tag release の正本は `github-release-package.yml` 1 本である
- 利用者向けの公開 Release asset は app ZIP のみとする
- `rescue-worker-artifact.yml` は worker 単体切り分け用として残す

## 12. release 後の最終確認

できれば、GitHub Release から落とした ZIP を展開して軽く見る。

- app が起動する
- 一覧が開く
- サムネ作成が動く
- rescue worker が起動できる

## 13. つまり scripts だけで足りるか

答え:
- `create_github_release_package.ps1` だけなら、正式 release 完了までは足りない
- `invoke_release.ps1` まで含めれば、clean worktree 前提ならかなり足りる

不足分:
- GitHub Actions 成功確認
- GitHub Release asset 確認
- Release 本文へ自動で入った worker pin 情報の見た目確認

## 14. 最短チェックリスト

1. clean worktree にする
2. `./scripts/invoke_release.ps1 -Version X.Y.Z.W`
3. 必要なら `GH_TOKEN` を入れて `./scripts/invoke_github_release_preview.ps1 -Ref workthree -Wait`
4. GitHub Actions の `github-release-package` 成功確認
5. GitHub Release の app asset と `Bundled Rescue Worker` 本文ブロック確認
6. 必要なら `rescue-worker-artifact` を手動実行して worker 単体確認

## 15. 今後の改善余地

- release 本文へ app / worker の対応情報を自動展開する
- release 結果の GitHub 確認まで自動化する

## 16. todo

- 本命整理の実装計画は `Implementation Plan_release workflow統合_本命整理_2026-03-30.md` を正本とする
- app / worker を 1 workflow へ統合する時は、先にこの計画書のタスクリストを更新してから着手する
