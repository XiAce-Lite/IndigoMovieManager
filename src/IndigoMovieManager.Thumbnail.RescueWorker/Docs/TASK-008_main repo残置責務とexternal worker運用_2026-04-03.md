# TASK-008 main repo残置責務とexternal worker運用 2026-04-03

最終更新日: 2026-04-05

## 1. 目的

external repo 側で `RescueWorker` を自立させた後も、main repo 側が安全に worker を消費し、配布し、live 確認できる状態を先に固定する。

この文書は、`TASK-007 外部repo最小構成とCI最小フロー 2026-04-03` を受けて、consumer 側の正本運用をまとめたものである。

## 2. main repo に残す責務

### 2.1 起動と host 連携

- `ThumbnailRescueWorkerLauncher`
- `ThumbnailRescueWorkerLaunchSettingsFactory`
- `MainWindow.ThumbnailRescueWorkerLauncher`
- `Runtime` の host 基盤

main repo は worker の起動条件、引数生成、artifact 配置確認、起動失敗時の fail-fast を持つ。

### 2.2 UI と通常運用

- WPF 本体
- `QueueDb` と MainDB 連携
- watcher / tab / list の通常導線
- rescue 実行後の UI 同期

ここはユーザー体感テンポと実アプリ導線の中心なので、external repo へ出さない。

### 2.3 release と配布

- `scripts/create_github_release_package.ps1`
- `.github/workflows/github-release-package.yml`
- `scripts/invoke_release.ps1`
- app release 手順書

main repo は app 配布を主役にし、worker 同梱の最終判断を持つ。
2026-04-05 時点で、Public workflow は local worker source build へ戻らず、Private source が取れない時点で fail-fast する。

### 2.4 live 確認

- launcher が正しい worker を拾うか
- `compatibilityVersion` と manifest が一致するか
- UI から rescue 導線が最後までつながるか

この確認は worker repo 単体 CI では代替できないため、main repo 残しが正しい。

## 3. external repo 側の責務

- `Contracts / Engine / FailureDb / RescueWorker / Tests` の build と test
- worker publish、package、release asset 作成
- worker 側の `compatibilityVersion` 更新
- worker artifact manifest と `sha256` 生成

## 4. 正本運用

### 4.1 配布物の正本

- `Contracts / Engine / FailureDb` の正本は package
- worker 実行物の正本は GitHub Release asset
- Actions artifact は CI 確認用であり、本番 pin 先にしない

### 4.2 lock の持ち方

main repo は worker 依存を 1 か所へ pin する。

最低限、次を lock file に持つ。

- `Contracts / Engine / FailureDb` の package version
- worker artifact version
- `compatibilityVersion`
- `sha256`

これにより、release asset と package の組み合わせを main repo 側で決定的に再現できる。

## 5. compatibilityVersion と fail-fast

### 5.1 基本ルール

- `compatibilityVersion` は `int` の単独値とし、v1 では完全一致で判定する
- 迷ったら bump する
- algorithm 改善、性能改善、ログ追加、テスト追加だけでは bump しない

### 5.2 bump 必須

- CLI 引数の破壊的変更
- `result json` の必須項目変更
- `FailureDb schema` の破壊的変更
- worker artifact 内の配置規約変更
- main repo が前提にする manifest の意味変更

### 5.3 launcher の fail-fast 条件

main repo 側 launcher は次で即失敗する。

- lock file の version と取得物が一致しない
- `sha256` が一致しない
- manifest が読めない
- `compatibilityVersion` が一致しない
- 必須ファイルが artifact 内に存在しない

fail-fast 時は UI に曖昧な再試行を促さず、ログへ理由を明示する。

2026-04-03 時点で、`source worker not found` の起動スキップ時は
`published artifact invalid: compatibilityVersion mismatch.` のような診断理由を
launcher log に残す実装まで入っている。

さらに `TASK-009 worker lock file schemaとlauncher読取骨格 2026-04-03` で、
`rescue-worker.lock.json` を読む骨格と
`compatibilityVersion / sha256` の最小照合まで main repo に入れた。

2026-04-04 時点で、main repo の runtime 既定は
`artifact / bundled worker` を正本とし、
`project-build` は `IMM_THUMB_RESCUE_ALLOW_PROJECT_BUILD_FALLBACK=1`
を与えた local 開発時だけ候補に戻す運用へ寄せた。

同じく main repo の test project では、
2026-04-05 に `RescueWorkerApplicationTests.cs` を Private repo 側へ完全移送し、
worker csproj の `ProjectReference` も Public 側既定 test から外した。

## 6. 2 repo 同時変更フロー

1. external repo 側で先に branch を切る
2. breaking change なら `compatibilityVersion` を bump し、preview package / preview release asset を発行する
3. main repo 側で branch を切り、lock file を preview へ更新する
4. PR は相互リンクする
5. merge 順は `external repo -> preview ではない正式 release -> main repo pin 更新` に固定する

ローカル同時開発だけは例外で、local package source と local worker artifact path を使ってよい。

## 7. live 確認の最小チェックリスト

1. main repo が lock file の worker version と `sha256` を読める
2. launcher が対象 artifact を展開または参照できる
3. manifest の `compatibilityVersion` が一致する
4. UI から rescue 導線を起動できる
5. worker 実行後に result が戻り、UI と queue 状態が破綻しない
6. 失敗時ログに `version / path / mismatch reason` が残る

## 7.1 残置直参照の分類

2026-04-05 時点で、main repo に残る worker 直参照は次の 4 分類に整理できる。

### A. consumer 正本責務

- launcher
- lock file 読取
- marker / `compatibilityVersion` / `sha256` fail-fast
- private artifact / private release asset の同期
- app package への worker 同梱

これらは main repo が app を配る責務を持つ以上、残す方が自然である。

### B. 明示 opt-in の例外導線

- `project-build` fallback
- local worker source build

これらは既定導線ではなく、local 開発と切り分け用の例外である。
2026-04-05 時点で、`local worker source build` は Public 側の正面入口 `invoke_release.ps1` では使わせず、下位 script だけに閉じ込めている。

### C. bootstrap 橋渡し資産

- `scripts/bootstrap_private_engine_repo.ps1`

これは runtime 密結合ではなく、Public repo から Private repo seed を再生成する橋渡しである。

### D. 履歴資料

- 過去の `Docs/forHuman`
- 過去の `Docs/forAI`
- `Thumbnail/救済worker`

ここは runtime / build / release の blocker ではない。

## 7.2 Phase 6 の見方

したがって Phase 6 の残件は、
「runtime がまだ worker source に密結合している」ではない。

本当の残件は次である。

1. local source build 例外をどこまで縮退できるか判断する
2. 履歴資料を Public / Private の完成形説明へ順次置き換える

## 8. 結論

`TASK-007` が worker を外で自立させる計画なら、`TASK-008` は main repo 側でそれを安全に消費し、配布し、実機確認する計画である。

この整理により、external repo は worker の build/test/publish に集中でき、main repo は UI、体感テンポ、launcher、release、live 確認に集中できる。
