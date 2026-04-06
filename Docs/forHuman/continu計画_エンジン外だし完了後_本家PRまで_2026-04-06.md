# continu計画 エンジン外だし完了後 本家PRまで 2026-04-06

## 1. 目的

この文書は、2026-04-06 時点の到達点を前提に、
`IndigoMovieManager_fork` から本家へ PR を出すまでの残タスクを、
AI と人が同じ順序で追えるように整理した continu 計画である。

## 2. 先に結論

現在の主残件は、`installer live proof` である。

- 公開ミラー運用は成立している
- engine 外だし導線は大筋成立している
- AI 向けの公開運用手順と復旧手順も整備済みである
- したがって、次は installer の実機 proof を終わらせる
- それが終わったら、本家 PR 文面を作り、最終確認後に PR を出す

## 3. 現在の到達点

- Public repo から `Contracts / Engine / FailureDb / RescueWorker` の tracked source を外す方針は成立している
- build / release は private packages と worker artifact consume 前提へ寄せられている
- 公開ミラー運用の workflow fallback は修正済みである
- `PRIVATE_ENGINE_PUBLISH_RUN_ID = 23997659256` へ更新済みである
- 以下の run 成功を確認済みである
  - `24032239061`
  - `24032342401`
  - `24032500601`

## 4. 残タスク

### 4.1 最優先

1. クリーン環境 install proof
2. 旧版あり環境からの upgrade proof

### 4.2 その次

3. proof 結果を文書へ反映
4. 本家 PR 用説明文を作成
5. PR 前の最終確認
6. 本家へ PR

## 5. 実行順

### Step 1 installer live proof

確認したいこと:

- クリーン環境で install が通る
- 既存 runtime 状態で bundle が落ちない
- 旧版あり環境から upgrade が通る
- install 後の起動確認ができる

完了条件:

- install proof が 1 回通る
- upgrade proof が 1 回通る
- blocker が無い

### Step 2 文書反映

更新対象:

- `Docs/forHuman/インストーラー計画_完了確認メモ_2026-04-06.md`
- `Docs/forHuman/現タスク_エンジン外だし完了後に本家へPR_2026-04-06.md`

反映すること:

- proof 実施日
- 使用した setup
- 結果
- 残件が消えたか

### Step 3 本家 PR 用説明文作成

説明に入れる内容:

- Public repo から engine source を tracked 管理しない構成へ寄せた
- build / release は package / artifact consume 前提で成立する
- 公開配布は mirror / asset 側を正本にする
- 本家で追加作業が必要なら、その範囲を明示する

### Step 4 最終確認

確認項目:

- source zip に engine code が含まれない
- workflow preview が再現する
- installer proof が記録済みである
- AI 向け手順書が現状と矛盾しない

### Step 5 本家 PR

PR では以下を短く伝える。

- 何を変えたか
- なぜ必要か
- どこまで proof 済みか
- 本家側に必要な判断や作業の有無

## 6. 現時点の判断

2026-04-06 時点では、
`外だしそのもの` よりも `installer 完了条件の消化` がボトルネックである。

そのため、continu の正しい進め方は次である。

1. installer live proof
2. 文書更新
3. PR 文面作成
4. 本家 PR

## 7. 今やらないこと

- installer proof 未了のまま本家 PR を出す
- 公開ミラー release の揺れに引きずられて全体設計を再変更する
- Public repo を source build へ戻す

## 8. 関連文書

- `Docs/forHuman/現タスク_エンジン外だし完了後に本家へPR_2026-04-06.md`
- `Docs/forHuman/インストーラー計画_完了確認メモ_2026-04-06.md`
- `Docs/forHuman/AI向け_公開手順書_公開ミラーGitHubRelease運用_2026-04-06.md`
- `Docs/forHuman/AI向け_公開ミラーGitHubRelease復旧手順_run_id_release_tag_2026-04-06.md`
