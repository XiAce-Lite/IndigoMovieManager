# continu計画 エンジン外だし完了後 本家PRまで 2026-04-06

## 1. 目的

この文書は、2026-04-06 時点の到達点を前提に、
`IndigoMovieManager_fork` から本家へ PR を出すまでの残タスクを、
AI と人が同じ順序で追えるように整理した continu 計画である。

## 2. 先に結論

現在の主残件は、`本家向け PR 文面整理と起票準備` である。

- 公開ミラー運用は成立している
- engine 外だし導線は大筋成立している
- AI 向けの公開運用手順と復旧手順も整備済みである
- installer の実機 proof も 2026-04-07 に完了した
- したがって、次は本家 PR 文面を作り、必要なら最終 rehearsal の後に PR を出す

## 3. 現在の到達点

- Public repo から `Contracts / Engine / FailureDb / RescueWorker` の tracked source を外す方針は成立している
- build / release は private packages と worker artifact consume 前提へ寄せられている
- 公開ミラー運用の workflow fallback は修正済みである
- `PRIVATE_ENGINE_PUBLISH_RUN_ID = 23997659256` へ更新済みである
- 以下の run 成功を確認済みである
  - `24032239061`
  - `24032342401`
  - `24032500601`
- 2026-04-07 にローカル live proof を行い、`clean install` と `1.0.3.4 -> 1.0.3.5` upgrade の両方を確認済みである

## 4. 残タスク

### 4.1 最優先

1. 本家 PR 用説明文を作成する
2. 必要なら最新 `master` に対する最終 tag release rehearsal を行う

### 4.2 その次

3. PR 前の最終確認
4. 本家へ PR

## 5. 実行順

### Step 1 文書反映

更新対象:

- `Docs/forHuman/インストーラー計画_完了確認メモ_2026-04-06.md`
- `Docs/forHuman/現タスク_エンジン外だし完了後に本家へPR_2026-04-06.md`

反映すること:

- proof 実施日
- 使用した setup
- 結果
- 残件が消えたか

### Step 2 本家 PR 用説明文作成

説明に入れる内容:

- Public repo から engine source を tracked 管理しない構成へ寄せた
- build / release は package / artifact consume 前提で成立する
- 公開配布は mirror / asset 側を正本にする
- 本家で追加作業が必要なら、その範囲を明示する

### Step 3 最終確認

確認項目:

- source zip に engine code が含まれない
- workflow preview が再現する
- installer proof が記録済みである
- AI 向け手順書が現状と矛盾しない

### Step 4 本家 PR

PR では以下を短く伝える。

- 何を変えたか
- なぜ必要か
- どこまで proof 済みか
- 本家側に必要な判断や作業の有無

## 6. 現時点の判断

2026-04-07 時点では、
`installer 完了条件の消化` は終わり、`upstream へどう説明して渡すか` が主眼になった。

そのため、continu の正しい進め方は次である。

1. 文書更新
2. PR 文面作成
3. 必要なら最終 rehearsal
4. 本家 PR

## 7. 今やらないこと

- 公開ミラー release の揺れに引きずられて全体設計を再変更する
- Public repo を source build へ戻す

## 8. 関連文書

- `Docs/forHuman/現タスク_エンジン外だし完了後に本家へPR_2026-04-06.md`
- `Docs/forHuman/インストーラー計画_完了確認メモ_2026-04-06.md`
- `Docs/forHuman/AI向け_公開手順書_公開ミラーGitHubRelease運用_2026-04-06.md`
- `Docs/forHuman/AI向け_公開ミラーGitHubRelease復旧手順_run_id_release_tag_2026-04-06.md`
- `Docs/forHuman/本家PR文面案_エンジン外だしと公開ミラー運用_2026-04-07.md`
- `Docs/forHuman/完全自動化案_公開ミラーmanifest正本化_2026-04-07.md`
