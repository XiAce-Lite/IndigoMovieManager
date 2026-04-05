# 設計メモ main repo残置直参照棚卸し Public責務集中 2026-04-05

最終更新日: 2026-04-05

## 1. 目的

Public repo 側にまだ残っている `RescueWorker` 直参照を棚卸しし、
次の 3 点を固定する。

1. 正本として残す参照
2. 例外導線としてだけ残す参照
3. 将来さらに薄くできる参照

判断軸は一貫して次である。

- Public repo は app に機能を追加し、配る責務に集中する

## 1.1 2026-04-05 の追加判断

同日、Public repo 側に package consume mode を追加した。

- `ImmUsePrivateEnginePackages=true`
- `ImmPrivateEnginePackageSource=<feed or folder>`
- `ImmPrivateEnginePackageVersion=<version>`

これにより app / queue / runtime / tests は、既定では source project を見つつ、
明示時だけ `Contracts / Engine / FailureDb` を Private Engine packages から consume できる。
`Queue / Runtime` 自体は Public repo 側 project のまま残る。

## 2. 先に結論

2026-04-05 時点で、Public repo の既定 runtime / 既定 test / 既定 release は、
worker source build や worker project 直参照を前提にしない形まで進んでいる。

加えて、package consume mode により、Public repo の build 依存も
source project 依存から package 依存へ段階的に寄せられる入口ができた。

残っている直参照は主に次の 3 分類である。

1. Public repo の正本責務として残すもの
2. local 開発・切り分け用の明示 opt-in 例外
3. 歴史資料として残る doc 参照

したがって今の残件は、「runtime がまだ密結合」ではなく、
「例外導線と履歴資料をどう整理するか」の段階である。

## 3. 正本として残す直参照

### 3.1 launcher / engine-client

- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\ThumbnailRescueWorkerLaunchSettingsFactory.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\ThumbnailRescueWorkerArtifactLockFile.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\ThumbnailRescueWorkerLauncher.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\ThumbnailRescueWorkerJobJsonClient.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\MainWindow.ThumbnailRescueWorkerLauncher.cs`

理由:

- Public repo が worker 実行ファイルの所在、lock file、marker、`compatibilityVersion` を確認して起動する責務を持つため
- これは worker 実装依存ではなく consumer 側の正式責務である

### 3.2 release / lock / verify

- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\create_github_release_package.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\invoke_release.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\sync_private_engine_worker_artifact.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\test_private_engine_package_consume.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\verify_app_package_worker_lock.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\.github\workflows\github-release-package.yml`

理由:

- Public repo が app package に何を同梱し、どの worker version を pin するかを決める責務を持つため
- Private repo の publish 結果や package を消費する consumer 側処理であり、残す方が自然である

### 3.3 consumer 観点の test

- `C:\Users\na6ce\source\repos\IndigoMovieManager\Tests\IndigoMovieManager.Tests\ThumbnailRescueWorkerLauncherTests.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Tests\IndigoMovieManager.Tests\IsolatedEngineAttemptLiveTests.cs`

理由:

- Public repo 側の launcher、bundle、lock、fail-fast が壊れていないかを確認する test である
- worker の中身ではなく、Public 側 consumer としての振る舞い確認なので残す

## 4. 明示 opt-in の例外導線

### 4.1 project-build fallback

- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\ThumbnailRescueWorkerLaunchSettingsFactory.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Tests\IndigoMovieManager.Tests\ThumbnailRescueWorkerLauncherTests.cs`

扱い:

- 既定では無効
- `IMM_THUMB_RESCUE_ALLOW_PROJECT_BUILD_FALLBACK=1` の時だけ local opt-in

理由:

- Public repo の runtime 正本は `artifact / bundled worker`
- `project-build` は local 同時開発の救済導線に留める

## 5. 引退済みの橋渡し資産

- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\bootstrap_private_engine_repo.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\scripts\private-engine-seed\`

理由:

- Public repo から Private repo seed を再生成・同期する入口として使っていた
- 2026-04-05 時点で、Private repo 自身の clone + docs + scripts を正面入口にできたため引退した
- したがって、もはや current な残置直参照ではない

## 6. 歴史資料として残る参照

代表例:

- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forHuman\*.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\forAI\*.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Docs\Gemini\*.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\救済worker\*.md`

扱い:

- runtime / build / release に影響しないため、直ちに blocker ではない
- 今後 `Public / Private` の完成形説明へ置き換える時に順次整理する

## 7. 直近の削減候補

優先順は次である。

1. `project-build` fallback を、数回の安定 release 後にさらに縮退できるか見直す
2. 履歴資料の古い source path 直書きを Public / Private 完成形へ順次置換する

## 8. Phase 6 の完了条件

Phase 6 を実務上完了とみなしてよい条件は次である。

1. Public repo の既定 runtime が worker source を見ない
2. Public repo の既定 test が worker project 直参照を持たない
3. Public repo の既定 release が external artifact / bundled worker を正本にする
4. 残る worker source 直参照が「明示 opt-in」または「履歴資料」に分類できる
5. Private repo 側で worker 本体 test と publish が自立している
6. app / queue / runtime / tests が `ImmUsePrivateEnginePackages=true` で shared core (`Contracts / Engine / FailureDb`) の package consume へ切り替えられる

## 9. 今回の実務判断

2026-04-05 時点では、Public repo に残る worker 直参照は、
もはや「外だし未完了の証拠」ではなく、
consumer 側責務と移行期例外を明示した結果である。

したがって次の本命は、闇雲な参照削除ではない。

- Private repo 側 test へ完全移送できるものを順に減らす
- Public repo 側は app に機能を追加し、配る責務に集中する
- 履歴資料の更新を除けば、bootstrap / seed の橋渡しは引退済みとして扱う

この 2 本で進めるのが最も安全である。

## 10. 参照先

- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\Docs\Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\src\IndigoMovieManager.Thumbnail.RescueWorker\Docs\TASK-008_main repo残置責務とexternal worker運用_2026-04-03.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager\Thumbnail\Docs\設計メモ_engine-client責務表_Public本体責務集中_2026-04-04.md`
