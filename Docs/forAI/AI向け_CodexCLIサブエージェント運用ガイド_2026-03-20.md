# AI向け Codex CLI サブエージェント運用ガイド 2026-03-20

最終更新日: 2026-03-20

変更概要:
- `codex-cli 0.98.0` と OpenAI 公式 Codex docs を前提に、subagent の実運用手順を整理
- `workthree` ブランチでの役割分担と、`AGENTS.md` を前提にした prompt テンプレートを追加
- `custom agent` と `[agents]` 設定の最小例を追加

## 1. この資料の目的

- この資料は、`codex CLI` を使って AI 向けサブエージェントを安全に回すための運用ガイドである。
- 対象は、このリポジトリでの調整役、実装役、レビュー役の分担である。
- 最優先は、`workthree` 本線の体感テンポを壊さず、責務境界を崩さずに並列化の利点だけ取ることである。

## 2. 先に結論

- 2026-03-20 時点の `codex-cli 0.98.0` では、subagent 専用の top-level コマンドはない。
- ただし、Codex の current release では subagent workflow 自体は既定で有効であり、明示的に依頼すると CLI 内で subagent を spawn できる。
- CLI では `/agent` で agent thread を切り替えられる。
- built-in agent は `default`、`worker`、`explorer` の 3 つである。
- project 専用の custom agent は `.codex/agents/*.toml` で定義できる。
- subagent は親の sandbox / approval 設定を引き継ぐ。非対話実行では新しい承認が必要になった時点で失敗として親へ返る。

## 3. このリポジトリでの基本分担

- 親 agent
  - 調整役
  - 受け入れ判断
  - 最終要約
- `explorer`
  - 調査
  - 既存コードの棚卸し
  - 変更帯の洗い出し
- `worker`
  - 実装
  - テスト追加
  - 局所リファクタ
- custom `reviewer`
  - finding first の差分レビュー
  - read-only 固定推奨

この分担は、`Docs\Implementation Plan_完成形移行_超大粒度ロードマップ_2026-03-20.md` の調整役 / 実装役 / レビュー専任役の分離方針と揃える。

## 4. 最短の使い方

### 4.1 対話で始める

PowerShell 7.x で repo root へ移動してから始める。

```powershell
codex -C . --sandbox workspace-write --ask-for-approval on-request
```

最初の prompt は、必読資料を先に読ませた上で分担まで明示する。

```text
まず次を読んで前提を揃えてください。
- AGENTS.md
- .CODEX.md
- AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md
- AI向け_現在の全体プラン_workthree_2026-03-20.md

その後、以下の 3 本で進めてください。
1. explorer を 1 本起動して、対象ファイル帯と既存責務を棚卸し
2. worker を 1 本起動して、変更候補と最小テスト案を作成
3. reviewer を 1 本起動して、危険な逆流だけ先に列挙

全 agent の結果が揃ったら、重複を潰して実行順つきで統合してください。
```

### 4.2 実行中の操作

- `/agent`
  - active な agent thread を切り替える
- 親 thread への追加指示
  - `worker 1 を止めて`
  - `explorer に Watcher 側だけ再調査させて`
  - `reviewer の finding を優先度順で再整理して`

### 4.3 fork / resume

- 前回の続きは `resume`
- 同じ文脈から別案を試す時は `fork`

```powershell
codex resume --last
codex fork --last
```

`fork` は会話分岐であり、Git worktree の分離ではない。ファイル競合を避けたい時は別 worktree か別 branch を使う。

## 5. 非対話実行の使いどころ

`codex exec` でも subagent を使う指示自体は出せる。ただし新しい承認が必要になると止まるので、read-heavy な棚卸しや review に寄せる。

```powershell
codex exec -C . ^
  "AGENTS.md と .CODEX.md を読んだ後、explorer を 2 本使って Watcher と Thumbnail の責務境界を別々に棚卸しし、最後に統合してください。コード変更はしないでください。"
```

差分レビューだけなら `codex review` の方が素直である。

```powershell
codex review --base main "バグ、責務逆流、体感テンポ悪化、テスト不足を重大度順で指摘してください。"
```

## 6. custom agent の置き場所

- 個人専用
  - `~/.codex/agents/`
- project 専用
  - `.codex/agents/`

このリポジトリでは project 専用を推奨する。理由は、`AGENTS.md` と branch 方針を前提にした reviewer / planner を repo ごとに固定しやすいからである。

## 7. 最小設定例

### 7.1 `.codex/config.toml`

```toml
[agents]
max_threads = 4
max_depth = 1
job_max_runtime_seconds = 1800
```

この repo ではまず以下を推奨する。

- `max_threads = 4`
  - 親 1、本番並走 2、review 1 を想定
- `max_depth = 1`
  - 孫 agent を禁止して暴走を避ける

### 7.2 `.codex/agents/reviewer.toml`

```toml
name = "reviewer"
description = "差分レビュー専任。finding first で重大度順に返す。"
developer_instructions = """
あなたはレビュー専任です。
実装はしません。
必ず AGENTS.md と .CODEX.md を前提にし、
バグ、責務逆流、体感テンポ悪化、テスト不足を finding first で返してください。
"""
sandbox_mode = "read-only"
nickname_candidates = ["Atlas", "Delta", "Echo"]
```

### 7.3 `.codex/agents/planner.toml`

```toml
name = "planner"
description = "棚卸しと論点整理専任。コード変更は禁止。"
developer_instructions = """
あなたは棚卸し専任です。
コード変更はしません。
必ず AGENTS.md、.CODEX.md、AI向け_現在の全体プラン_workthree_2026-03-20.md を先に読み、
変更帯、禁止線、完了条件、最小テスト観点だけを短く整理してください。
"""
sandbox_mode = "read-only"
```

## 8. この repo 向けの prompt テンプレート

### 8.1 調整役が最初に投げる prompt

```text
まず必読資料を読んで前提を揃えてください。
- AGENTS.md
- .CODEX.md
- AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md
- AI向け_現在の全体プラン_workthree_2026-03-20.md

対象テーマは <ここにテーマ> です。

進め方:
1. planner か explorer を使って、対象ファイル帯と禁止線を先に棚卸し
2. worker を 1 本だけ起動して、変更と最小テストを実装
3. reviewer を起動して、finding first でレビュー
4. 最後に親 thread で、差分要約、テスト、残リスクを 3 点以内で統合
```

### 8.2 review 専任だけ回したい時

```text
current branch と main の差分を見てください。
reviewer を使って以下を並列確認してください。
1. バグ
2. 責務逆流
3. 体感テンポ悪化
4. テスト不足

全結果を重大度順で統合し、finding first で返してください。
問題がなければ、残留リスクかテストギャップだけ短く残してください。
```

## 9. 運用上の注意

- 同じファイル帯を複数 worker に触らせない
- 調査系は並列化してよいが、実装系は 1 レーン 1 worker を基本にする
- `fork` は会話分岐であり、物理的な作業ツリー分離ではない
- 本当に独立した並走が必要なら `git worktree` か `codex cloud exec` を検討する
- 非対話実行で edit 系 subagent を使う時は承認待ちで止まりやすい
- Windows は公式には experimental 扱いである。とはいえ、この repo の既定運用は `PowerShell 7.x` で統一する

## 10. この repo での推奨パターン

### 10.1 調査だけを速くしたい時

- 親 1
- `explorer` 2
- `reviewer` 1

### 10.2 実装 1 本を安全に進めたい時

- 親 1
- `planner` または `explorer` 1
- `worker` 1
- `reviewer` 1

### 10.3 いま避けるべき形

- `worker` を複数本立てて同一フォルダ帯を同時編集
- `max_depth` を上げて孫 agent まで再帰委任
- `danger-full-access` 前提の reviewer
- `AGENTS.md` を読ませずに generic prompt だけで流すこと

## 11. 参考情報

### 11.1 ローカル確認結果

- 確認日
  - 2026-03-20
- 確認した CLI
  - `codex-cli 0.98.0`
- 確認コマンド
  - `codex --help`
  - `codex exec --help`
  - `codex fork --help`
  - `codex resume --help`
  - `codex review --help`
  - `codex cloud exec --help`

### 11.2 公式資料

- Codex CLI
  - https://developers.openai.com/codex/cli
- Codex Subagents
  - https://developers.openai.com/codex/subagents
- ChatGPT plan 上の Codex 概要
  - https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan
