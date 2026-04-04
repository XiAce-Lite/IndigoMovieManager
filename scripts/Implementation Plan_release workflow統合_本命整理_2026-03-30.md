# Implementation Plan_release workflow統合_本命整理_2026-03-30

最終更新日: 2026-04-03

変更概要:
- app / worker の release workflow を 1 本へ寄せる本命整理の実装計画
- 入口、責務、release asset 添付を 1 workflow へ集約する方針を整理
- tag release では private release asset を正本にし、preview では run-id artifact を残す方針を追記
- 実装前に確認すべき todo をタスクリスト化
- 利用者向け Release asset は app ZIP のみにする運用を追記
- release helper が worker lock の転記用 markdown を出す前提を追記

## 1. 目的

- release 周りの責務を 1 workflow へ集約する
- app ZIP と worker ZIP を同じ release job でまとめて扱う
- 将来の保守で「どちらの workflow を直すか」で迷わない状態にする

## 2. 現在の構成

- `.github/workflows/github-release-package.yml`
  - app ZIP 作成
  - GitHub Release asset 添付
- `.github/workflows/rescue-worker-artifact.yml`
  - worker ZIP 作成
  - Actions Artifact 保存
  - GitHub Release asset 添付

現状でも配布はできるが、
- 同じ tag で 2 workflow が並行実行される
- release 添付責務が 2 か所へ割れている
- 遅延や失敗時の切り分けが少し分かりにくい

## 3. 本命整理の方針

- `github-release-package.yml` を release 正本 workflow にする
- 利用者向け Release asset は app ZIP のみにする
- worker 単体 ZIP は `workflow_dispatch` の補助 workflow と Actions Artifact 側へ残す
- `rescue-worker-artifact.yml` は削除せず、必要なら `workflow_dispatch` 専用の補助 workflow へ縮退する

## 4. 完了後の理想像

- tag push 時に release workflow は 1 本だけ見る
- 利用者向け Release asset は app ZIP だけ見る
- worker 単体 ZIP は必要時だけ手動 workflow から取得する
- asset 添付 step は app 側だけ見る
- 失敗時は 1 workflow のログだけ追えば足りる
- Release 本文の worker pin 情報も同じ workflow が正本になる

## 5. 実装スコープ

入れるもの:
- `github-release-package.yml` を app ZIP 専用の release 正本に固定
- `rescue-worker-artifact.yml` の役割縮退
- release 手順 doc 更新
- `release-worker-lock-summary-*.md` を `body_path` で読み、worker pin 情報を Release 本文へ自動反映
- `release-worker-lock-summary-*.md` を artifact としても保存し、workflow_dispatch で本文 preview を確認
- `release-worker-lock-summary-*.md` を run summary にも出し、artifact download なしで preview を確認
- token 環境変数だけで preview run を起動できる helper script を置く

入れないもの:
- release 本文の自動整形強化
- app / worker 対応表の自動展開
- workflow 以外の build スクリプト全面再設計

補足:
- app / worker 対応表の全面自動生成までは入れないが、`create_github_release_package.ps1` と `invoke_release.ps1` が `release-worker-lock-summary-*.md` を出し、workflow の `body_path` 正本として使う
- 同じ markdown を artifact にも載せ、実 release 前の GitHub 上 preview を可能にする
- 同じ markdown を run summary にも載せ、確認導線をさらに短くする

## 6. 実装案

### 案A: release 正本 + worker 手動補助

- `github-release-package.yml` は app ZIP だけを Release へ添付
- tag push 時は private release asset を tag 名で同期してから app ZIP を作る
- `github-release-package.yml` は worker lock summary markdown を Release body へも反映
- `github-release-package.yml` は worker lock summary markdown を preview artifact にも残す
- `github-release-package.yml` は worker lock summary markdown を run summary にも表示する
- ローカルからは `invoke_github_release_preview.ps1` で workflow_dispatch を起動できる
- `rescue-worker-artifact.yml` は `workflow_dispatch` 専用で worker ZIP を作る
- worker ZIP は Actions Artifact として取得する

利点:
- 利用者向け公開面がきれい
- worker 外だし方針とも相性がよい

注意:
- worker 単体 ZIP は公開 Release asset に出ない

### 案B: 後段 job 添付

- app job と worker job は分ける
- 後段の release job で artifact download 後にまとめて添付する

利点:
- job 分離で見通しがよい

注意:
- 今の規模だと少し大げさ

結論:
- 利用者向けの親切さを優先し、案Aを本命とする

## 7. `rescue-worker-artifact.yml` の扱い

候補は 2 つ。

1. `workflow_dispatch` 専用へ縮退
- tag trigger を外す
- worker 単体検証や配布確認用として残す

2. 削除
- 完全に `github-release-package.yml` へ統合する

おすすめ:
- まず 1
- 運用が安定してから削除判断

## 8. テスト観点

- tag push で workflow が 1 本の release 主導に寄ること
- app ZIP が Release asset に載る
- worker 単体手動 workflow を残すなら、`workflow_dispatch` で ZIP が作れる
- doc の説明が現行運用と一致する

## 9. リスク

- worker ZIP の導線が分かりにくいと、開発者が「どこで取るか」に迷う
- 旧 `rescue-worker-artifact.yml` を残す場合、説明を更新しないと二重経路に見える

## 10. 先に決めること

- `rescue-worker-artifact.yml` を縮退で残すか、即削除するか
- worker ZIP を Release asset に出さない運用で確定するか
- release workflow 名を現状維持にするか

## 11. タスクリスト

- [x] Task 1: `github-release-package.yml` を app ZIP 専用の release 正本に固定
- [x] Task 2: worker ZIP は `workflow_dispatch` 専用の補助 workflow へ寄せる
- [x] Task 3: `rescue-worker-artifact.yml` を `workflow_dispatch` 専用へ縮退、または削除
- [x] Task 4: `scripts/正式Release手順_GitHubTag運用_2026-03-30.md` を統合後仕様へ更新
- [x] Task 5: `Docs/forHuman/GitHubRelease_実行可能バイナリ配布手順_2026-03-15.md` を統合後仕様へ更新
- [ ] Task 6: 実 tag または検証用 branch で workflow 実行ログを確認
- [x] Task 7: 旧二重 workflow 前提の記述が残っていないかレビュー

## 11.1 現在の進捗メモ

- tag release の正本は `github-release-package.yml` へ集約した
- 利用者向け Release asset は app ZIP のみとした
- `rescue-worker-artifact.yml` は worker 単体確認用の `workflow_dispatch` に縮退した
- `invoke_release.ps1` も app release 優先の既定動作へ寄せ、worker 単体 ZIP は明示指定時だけローカル生成する
- 残件は、実際の GitHub Actions 実行で app ZIP のみが Release に出ること、Release 本文へ `Bundled Rescue Worker` が入ること、worker 手動 workflow が生きていることの確認である
- 残件は、workflow_dispatch で preview artifact が取れることも含めて確認する

## 12. この計画の結論

- 本命整理は小〜中粒度
- 実装本体よりも、workflow 役割の明確化と doc 整合が重要
- 先に案Aで集約し、`rescue-worker-artifact.yml` は段階的に縮退するのが安全
