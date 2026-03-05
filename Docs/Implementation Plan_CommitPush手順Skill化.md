# Implementation Plan（このプロジェクトの commit & push 手順 Skill 化）

## 1. 目的
- このプロジェクト向けの `commit` と `push` を、毎回同じ品質で実行できる Skill にする。
- 手順の揺れ（add漏れ、メッセージ揺れ、push先ミス）を減らす。
- 失敗時の原因切り分け手順まで Skill に含め、無駄な再試行を防ぐ。

## 2. 対象範囲
- 対象
  - ローカル変更の確認
  - コミット対象の選別
  - コミットメッセージ生成
  - `origin` への push
  - 失敗時の復旧フロー（非 fast-forward / 認証 / コンフリクト）
- 非対象
  - リリース作業
  - GitHub PR 作成
  - DB や成果物の内容レビューそのもの

## 3. 前提（現状）
- 現在ブランチ: `master`
- 主 push 先: `origin`（URLは環境依存値）
- 追加 remote: `upstream_local`
- Skill 配置先: `<SKILLS_ROOT>`
- 実値は `.local/LocalEnvironment.md`（git管理外）で管理する。
- 共通ドキュメントでは次のプレースホルダを使う。
  - `<REPO_ROOT>`: 対象リポジトリのルート
  - `<SKILLS_ROOT>`: Skill ルートディレクトリ
  - `<ORIGIN_REMOTE_URL>`: `origin` の push URL

## 4. 作成する Skill の設計
- Skill 名（案）: `indigo-commit-push`
- ディレクトリ:
  - `<SKILLS_ROOT>/indigo-commit-push/SKILL.md`
  - `<SKILLS_ROOT>/indigo-commit-push/agents/openai.yaml`
  - `<SKILLS_ROOT>/indigo-commit-push/scripts/commit_push.ps1`
  - `<SKILLS_ROOT>/indigo-commit-push/references/commit_message_rules.md`
- 方針:
  - 手順本文は `SKILL.md` に最小限で記述する。
  - 実行の再現性が必要な箇所は `scripts/commit_push.ps1` に寄せる。
  - プロジェクト固有ルールは `references/commit_message_rules.md` に分離する。

## 5. Skill に入れる標準フロー
1. 事前確認: `git status --short --branch` で状態確認
2. 差分確認: `git diff --name-status` と `git diff --cached --name-status`
3. 未追跡確認: 未追跡ファイルを追加対象にするかユーザーへ確認する
4. ステージング: 対象ファイルを明示して `git add`
5. コメント確認: コミットコメント（メッセージ）をユーザーへ確認する
6. コミット: 確認済みコメントで `git commit`
7. 同期確認: 必要時のみ `git pull --rebase origin <branch>`
8. push: `git push origin <branch>`
9. 結果出力: 反映ブランチ、コミットID、未追跡ファイル有無を表示

## 6. 実装フェーズ

### Phase 1: 具体例確定
- ユースケースを3つ固定する。
  - 通常変更を commit & push
  - 競合なしの rebase 後 push
  - non-fast-forward 発生時の復旧
- トリガー文言を定義する。
  - 例: 「この project を commit して push して」

成果物:
- 例示セット（入力文・期待動作・期待出力）

### Phase 2: Skill 雛形生成
- `init_skill.py` で Skill 雛形を作成する。
- `name` / `description` をトリガーしやすい内容で確定する。

成果物:
- Skill フォルダ一式（`SKILL.md`, `agents/openai.yaml`, `scripts`, `references`）

### Phase 3: 手順実装
- `SKILL.md` に高レベル手順を書く（冗長説明は書かない）。
- `scripts/commit_push.ps1` に実行手順を実装する。
  - PowerShell 7 前提
  - UTF-8（BOMなし）+ LF
  - 失敗時は原因別メッセージを返す
- `references/commit_message_rules.md` にコミット文ルールを定義する。

成果物:
- 実行可能な Skill 初版

### Phase 4: 検証
- `quick_validate.py` で Skill 構造を検証する。
- 実リポジトリで以下を確認する。
  - 変更あり/なし判定
  - push 成功
  - 失敗時メッセージの妥当性

成果物:
- 検証ログ（成功ケース/失敗ケース）

## 7. 受け入れ条件
- 「この project を commit & push して」の指示で Skill が安定発火する。
- commit 前に「未追跡ファイルを追加するか」の確認が必ず入る。
- commit 前に「コミットコメント」の確認が必ず入る。
- commit 前チェック、commit、push、失敗時案内まで一貫して実行できる。
- 手順が手打ち依存にならず、同じ入力で同じ結果になる。

## 8. リスクと対策
- リスク: `master` 直 push ルールが将来変わる
  - 対策: `references` 側にブランチ運用ポリシーを分離し更新しやすくする
- リスク: 認証切れで push 失敗
  - 対策: 失敗検知時に再認証手順（`gh auth login` または credential 再設定）を返す
- リスク: 競合解消の自動化が過剰
  - 対策: コンフリクト時は自動解消せず、停止して手動解消を明示する

## 9. 実施順（推奨）
1. Phase 1（具体例確定）
2. Phase 2（雛形生成）
3. Phase 3（手順実装）
4. Phase 4（検証）

## 10. 次アクション（この計画の直後）
1. Skill 名を最終確定する（`indigo-commit-push` で確定するか）
2. `description` のトリガー文言を確定する
3. Phase 2 から実装を開始する
