# AGENTS.md
- 人類とAIの信頼関係を築く
- アプリケーションの作成とは新たな宇宙を想像する神の仕事

## 基本ルール
- 日本語で回答、ドキュメントも日本語で
- コードには日本語で流れ重視のコメントを記載する
- MVVMを基本とするが開発者がコードを掴みやすいようにする事の方が重要
- 文字コードと改行は「UTF-8 (BOMなし) + LF」を使用する
- チャットコメントのリンクは必ず生パスで記載する（Markdownリンク形式は使わない）

## Mermaid表示ルール
- Mermaidプレビューは `Markdown Preview Mermaid Support`（`bierner.markdown-mermaid`）を標準とする
- Mermaid確認は VS Code の Markdown Preview で行う
- Mermaid系の他拡張（別レンダラ）は同時有効化しない（競合回避）
- Mermaid記法は互換性優先で記述する（`subgraph` 内 `direction` や記号多用ラベルは避ける）

## 開発方針（VS2026）
- このプロジェクトは Visual Studio 2026 前提で開発する
- ビルド/テストのプラットフォームは `x64` に統一する（`Any CPU` は使わない）

## ビルド失敗時ルール
- ビルド失敗時は原因を先に特定してから再試行する
- ロックされている場合はユーザーが実行中の可能性を考慮し確認する
- フォーマット起因が疑わしい場合は `CSharpier` を実行して整形する

## 実行環境ルール
- PowerShellはUTF-16変換を防ぐため7.x.xを使用する
- ネット検索はコンテキスト書き換えや汚染を避けるため、必ずサンドボックス内でのみ行う
- AIエージェントは必要に応じて `C:\python\work` の Python 作業環境を利用してよい
- Python作業環境を使う場合は、`C:\python\Open-Initialize-PythonWork.cmd` で初期化し、通常作業は `C:\python\Open-PythonWork.cmd` を利用する
- Python関連コマンドを直接実行する場合も、原則として `C:\python\work\.venv` の仮想環境を前提にする

## スキル
- 必要に応じて `.agent\skills` を参照・作成する
- GitHub Release の preview 実行、tag 公開、release 失敗からの復旧を行う時は、`.agent\skills\indigomoviemanager-github-release\SKILL.md` を必ず参照し、その手順に従う

## 実行環境ルール
- PowerShellはUTF-16変換を防ぐため7.x.xを使用する

## プロジェクトルール
- WhiteBrowserの互換プログラムとして開発する
- WhiteBrowserのDB(*.wb)は変更しない

## 主要パス
- 実行時ログの出力先は `%LOCALAPPDATA%\IndigoMovieManager\logs\`
- `launchSettings.json` の場所は `Properties\launchSettings.json`

## ブランチ運用方針（AI必読）
- このブランチで作業するAIは、着手前に `AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md` を必ず読む
- 作業の大粒度優先順位と現在の全体計画は `AI向け_現在の全体プラン_workthree_2026-03-20.md` を正本として確認する
- このブランチは**開発本線**として、ユーザーが感じるテンポ感を最優先に、UI を含む高速化と安定化を進める

## 正本プランの見方（AI必読）
- 全体の優先順位と着手順の正本は `AI向け_現在の全体プラン_workthree_2026-03-20.md`
- ブランチの判断基準と禁止線の正本は `AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md`
- UI の詰まり解消と抜本高速化の正本は `Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md`
- `skin` 個別の高速化と保存分離の正本は `WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md`

## 2チーム体制（AI必読）
- 本線チームは `AI向け_現在の全体プラン_workthree_2026-03-20.md` と `Docs\forAI\Implementation Plan_UIを含む高速化のための抜本改善プラン_2026-04-17.md` を正本として進める
- スキンチームは `WhiteBrowserSkin\Docs\Implementation Plan_skin切り替え高速化_DB保存分離先行_2026-04-13.md` を正本として進める
- 本線チームは一覧、Watcher、Queue、起動、画像供給、UIスレッドの簡素化を主担当とする
- スキンチームは `refresh`、stale、catalog、skin API、保存分離、trace 観測を主担当とする
- どちらのチームも、着手前に全体正本を確認し、局所最適が全体優先順位と衝突しないことを確認する
- 共有ルールとして、DB 分離は主因ではなく土台施策と位置づけ、`refresh` / stale / catalog / diff-first UI を先に評価する

## ドキュメント運用ルール
- 今回作成したAI向け理解書・フロー資料・フォルダ構成書は、関連コード変更時に必ず随時更新する
- 実装とドキュメントの差分を放置せず、同一PR/同一コミット系列で整合を取る
- AI向け資料を更新した場合は、対応する資料（01〜04/フロー資料/構成書）を更新日付きで明示し、変更概要を残す

## コミット時ルール
- コミット前に `git diff --check` と `git status --short` を確認し、意図しない差分を混ぜない
- コミット前にローカル固有情報を確認する
  - 絶対パス
  - ユーザー名
  - メールアドレス
  - ローカル環境名
  - `.local` 配下参照
- 実動画依存のテスト・playground・script は、既定値にローカル絶対パスを直書きしない
- ドキュメントやサンプルでリポジトリ配下の例を書く時は、`%USERPROFILE%\source\repos\...` ベースで統一する
- ユーザー依存の例やローカル作業パスは、`%USERPROFILE%` / `%USERNAME%` ベースで統一し、`C:\Users\<ユーザー名>\...` を直書きしない
- コミットメッセージは日本語で、1コミット1目的を基本とする
- 大きな作業でも、意味の異なる変更はコミットを分ける
- コミット前に必要なドキュメント更新を同じコミット系列へ含める
- `Author` / `Committer` は GitHub ハンドル名 + `noreply` を基本とし、既定では `T-Hamada0101 <T-Hamada0101@users.noreply.github.com>` を使用する
- PowerShell からコミット・amend する時は、環境変数の一時設定より `git -c user.name="T-Hamada0101" -c user.email="T-Hamada0101@users.noreply.github.com" commit --author="T-Hamada0101 <T-Hamada0101@users.noreply.github.com>" ...` を優先し、`Author` / `Committer` の取り違えを防ぐ
- 既存コミットを修正する場合は、内容変更が無くても `Author` / `Committer` の確認を行う
- push 前に、公開して問題ない情報だけが履歴と差分に含まれていることを再確認する
# 🔥 各AIエージェントへの指令（必読！） 🔥
あなた（AIエージェント）は、自分の名前に対応する以下のドキュメントを**必ず**読み、そこに書かれたペルソナ（人格・口調）に従ってユーザーと対話、コード記述、ドキュメント作成を行ってください！

- **Gemini のあなたはここを読め！** 👉 [.GEMINI.md](.GEMINI.md)
- **Claude / Opus のあなたはここを読め！** 👉 [.CLAUDE.md](.CLAUDE.md)
- **Codex のあなたはここを読め！** 👉 [.CODEX.md](.CODEX.md)
- **このブランチで作業する全AIはここも読め！** 👉 [AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md](AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md)


## `.local` の運用ルール
- 保管場所: `C:\Users\osakacenter\source\repos\MyLab\.local`
- 用途: 環境固有情報、機密情報（APIキー、トークン、接続文字列、資格情報など）の保管専用。

## 取り扱い方針
- `.local` 配下の実データは Git にコミットしない。
- `.local` 配下の内容をドキュメント、Issue、チャットへそのまま貼り付けない。
- 共有が必要な場合は、値を除いたテンプレート（例: `.example`）のみ共有する。
- ログ出力やエラー出力に機密値を含めない。

## 補足
- `.local` はローカル環境専用ディレクトリとして扱う。
- 既存ツールやスクリプトで参照する場合も、機密値の平文露出を避ける。
