# forHuman README

最終更新日: 2026-04-05

このフォルダは、このコードベースを人が追い始める時の入口です。
通常の全体理解と、`workthree -> master` 統合前の説明導線を分けて扱います。

## 統合前に最初に読む順番

1. **[master統合前_workthree変更説明とfork元統合ガイド_2026-04-05.md](master統合前_workthree変更説明とfork元統合ガイド_2026-04-05.md)**
2. **[workthree統合proof checklist_PrivateEngine連携_2026-04-05.md](workthree統合proof%20checklist_PrivateEngine連携_2026-04-05.md)**

## 通常の全体理解で最初に読む順番

1. **[ProjectOverview_2026-03-29.md](../forHuman/ProjectOverview_2026-03-29.md)**
2. **[DevelopmentSetup_2026-02-28.md](DevelopmentSetup_2026-02-28.md)**
3. **[Architecture_2026-02-28.md](Architecture_2026-02-28.md)**
4. **[ProjectFilesAndFolders_2026-04-01.md](ProjectFilesAndFolders_2026-04-01.md)**
5. **[DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)**

## 目的別の入口

- **[master統合前_workthree変更説明とfork元統合ガイド_2026-04-05.md](master統合前_workthree変更説明とfork元統合ガイド_2026-04-05.md)**
  - `workthree` を `master` へ統合し、fork 元へ PR を出す前に、何が変わり何を先に決めるべきかを短時間で共有する資料です。
- **[workthree統合proof checklist_PrivateEngine連携_2026-04-05.md](workthree統合proof%20checklist_PrivateEngine連携_2026-04-05.md)**
  - Public / Private 分離運用がどこまで live 成功しているかを、run id 単位で短く確認する資料です。
- **[ProjectOverview_2026-03-29.md](../forHuman/ProjectOverview_2026-03-29.md)**
  - 新規参入者向けの正本です。最初に読む 1 本として使います。
- **[DevelopmentSetup_2026-02-28.md](DevelopmentSetup_2026-02-28.md)**
  - 環境構築、ビルド、最初の起動確認を見る時の入口です。
- **[ProjectFilesAndFolders_2026-04-01.md](ProjectFilesAndFolders_2026-04-01.md)**
  - どのフォルダに何があるかを見たい時の入口です。
- **[Architecture_2026-02-28.md](Architecture_2026-02-28.md)**
  - 各プロジェクトと責務の分担を見たい時の入口です。
- **[DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)**
  - `*.wb` / QueueDB / FailureDb の役割を見たい時の入口です。
- **[人間向け_大粒度フロー_DBとプロジェクト_現状と完成形_2026-03-20.md](人間向け_大粒度フロー_DBとプロジェクト_現状と完成形_2026-03-20.md)**
  - DB とプロジェクトのつながりを大粒度で追う資料です。
- **[VideoIndexRepairOverview_2026-03-06.md](../Gemini/VideoIndexRepairOverview_2026-03-06.md)**
  - インデックス修復の流れを掴みたい時の入口です。
- **[Migration_from_WhiteBrowser_Notes_2026-03-25.md](../Gemini/Migration_from_WhiteBrowser_Notes_2026-03-25.md)**
  - WhiteBrowser 互換まわりの考え方を確認したい時に見ます。
- **[サムネイルが作成できない動画対策.md](サムネイルが作成できない動画対策.md)**
  - 困りごとベースでサムネイルの対処を見たい時に使います。

## このフォルダに置くもの

- 人が最初に読む概要資料
- セットアップ、構成、仕様、運用の入口資料
- 全体理解を助ける簡潔な説明資料

## 詳しく追いたい時の次の入口

- AI向けの進行中メモや実装計画を見る
  - **[../forAI/README.md](../forAI/README.md)**
- テンション高めの背景説明や経緯を見る
  - **[../Gemini/README.md](../Gemini/README.md)**
- Watcher 領域を詳しく見る
  - **[../../Watcher/README.md](../../Watcher/README.md)**
- Thumbnail 領域を詳しく見る
  - **[../../Thumbnail/README.md](../../Thumbnail/README.md)**
