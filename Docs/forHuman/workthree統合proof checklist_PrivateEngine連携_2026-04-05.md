# workthree統合 proof checklist PrivateEngine連携 2026-04-05

最終更新日: 2026-04-05

## 1. この文書の目的

この文書は、`workthree` を `master` へ統合し、その後に fork 元へ PR を出す前に、

- Public / Private 分離運用がどこまで実際に動いたか
- 何を proof として添付すれば説明が通るか

を 1 枚で確認するための checklist である。

この文書は設計説明ではなく、**動作実績の確認票**として使う。

補足:

- installer は 2026-04-05 時点で `WiX v1` の proof まで
- つまり `install / upgrade / uninstall` と `ZIP + bundle exe` の release proof を対象にする
- `self-update` と `custom BA` はこの checklist の対象外

## 2. 先に結論

2026-04-05 時点で、少なくとも current fork では次を確認済みである。

1. Private repo の tag release で worker ZIP と `Contracts / Engine / FailureDb` package が同時に GitHub Release asset へ載る
2. Public repo の preview workflow が、その Private release asset を pin して app package を作れる
3. preview の summary から、worker だけでなく engine package version まで追える

つまり、fork 元へ伝えるべき状態は

- 「これから分離を試す」ではなく
- 「既に Public / Private 境界で live 成功したものを統合する」

である。

## 3. proof checklist

### 3.1 Private release asset 成功

- repo: Private repo (`IndigoMovieEngine`)
- tag: `v1.0.3.6-private.1`
- run: `23993219143`
- conclusion: `success`

確認できた release asset:

- `IndigoMovieManager.Thumbnail.RescueWorker-v1.0.3.6-private.1-win-x64-compat-2026-03-17.1.zip`
- `IndigoMovieEngine.Thumbnail.Contracts.1.0.3.6-private.1.nupkg`
- `IndigoMovieEngine.Thumbnail.Engine.1.0.3.6-private.1.nupkg`
- `IndigoMovieEngine.Thumbnail.FailureDb.1.0.3.6-private.1.nupkg`

意味:

- worker 単体だけでなく shared core package も Private 側 release asset を正本にできる

### 3.2 Public preview 成功

- repo: Public repo (`IndigoMovieManager_fork`)
- branch: `workthree`
- workflow: `github-release-package`
- input: `private_engine_release_tag=v1.0.3.6-private.1`
- run: `23993260850`
- conclusion: `success`

意味:

- Public 側が worker と package を同じ Private release asset から同期し、app package 作成まで通った

### 3.3 preview summary で確認したこと

`github-release-body-preview` artifact の summary で、少なくとも次を確認した。

- `Source: github-release-asset`
- `Version: v1.0.3.6-private.1`
- `CompatibilityVersion: 2026-03-17.1`
- `EnginePackageSource: github-release-asset`
- `EnginePackageVersion: 1.0.3.6-private.1`

意味:

- worker だけでなく engine package 側も、preview 結果から追跡できる

### 3.4 local consume 確認

Public repo で次を確認済み。

- `scripts/test_private_engine_package_consume.ps1`
  - `Contracts / Engine / FailureDb` package consume build/test 成功
- `scripts/create_github_release_package.ps1`
  - `-PreparedWorkerPublishDir`
  - `-PreparedPrivateEnginePackageDir`
  を指定した local package 作成成功
- `scripts/invoke_release.ps1 -DryRun`
  - Private package consume 前提の release helper 成功

意味:

- GitHub Actions だけでなく、手元でも same route を再現できる

### 3.5 same-tag 本番前提の Private release 追加確認

- tag: `v1.0.3.6`
- run: `23993334917`
- conclusion: `success`

意味:

- Public 本番 tag release が同名 tag の Private release asset を取りに行く前提も、Private 側だけなら成立している

## 4. fork元へ添付すると分かりやすいもの

最小セットは次でよい。

1. `master統合前_workthree変更説明とfork元統合ガイド_2026-04-05.md`
2. この `workthree統合proof checklist_PrivateEngine連携_2026-04-05.md`
3. 必要なら `scripts/README.md` の relevant 節

## 5. この proof でまだ言っていないこと

この proof は、fork 元の GitHub Settings を一切不要にするものではない。

fork 元で本当に必要なものは別途ある。

- `INDIGO_ENGINE_REPO_TOKEN`
- `private_engine_release_tag` または `private_engine_run_id`
- Private repo 名 / owner の整合

つまり、この文書は

- **統合候補が live 成功している**

ことの proof であり、

- **fork 元の設定作業が不要**

と言っている文書ではない。

## 6. 使い方

fork 元へ説明する時は次の順がよい。

1. `master統合前_workthree変更説明とfork元統合ガイド_2026-04-05.md`
2. この proof checklist
3. その後に workflow / scripts / docs へ進む

これで、最初の 5 分で

- 何が変わったか
- 何が既に動いたか
- 何を fork 元で設定すればよいか

を揃えやすくなる。
