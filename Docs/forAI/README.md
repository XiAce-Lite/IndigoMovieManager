# forAI README

最終更新日: 2026-03-28

このフォルダは、AI や実装担当が着手前に読む資料を集める場所です。
進行中の計画、調査結果、作業指示、レビュー結果は基本的にここへ寄せます。

## 着手前にまず見るもの

1. **[../../AGENTS.md](../../AGENTS.md)**
2. **[../../AI向け_現在の全体プラン_workthree_2026-03-20.md](../../AI向け_現在の全体プラン_workthree_2026-03-20.md)**
3. **[ドキュメント案内_人向け_AI向け_2026-03-12.md](ドキュメント案内_人向け_AI向け_2026-03-12.md)**

## まず読むと迷いにくい資料

- **[ドキュメント案内_人向け_AI向け_2026-03-12.md](ドキュメント案内_人向け_AI向け_2026-03-12.md)**
  - 人向け入口と AI 向け入口をまとめた案内です。
- **[AI向け_大機能詳細理解書_2026-03-07.md](AI向け_大機能詳細理解書_2026-03-07.md)**
  - 起動、Watcher、Thumbnail、検索の大きな責務をまとめた理解書です。
- **[Implementation Plan_完成形移行_超大粒度ロードマップ_2026-03-20.md](Implementation%20Plan_完成形移行_超大粒度ロードマップ_2026-03-20.md)**
  - 完成形へ向かう責務移行の大粒度ロードマップです。
- **[AI向け_運用ボード_完成形移行_2026-03-20.md](AI向け_運用ボード_完成形移行_2026-03-20.md)**
  - 作業の帯分けと役割分担を見る時の入口です。
- **[ThumbnailLogic_2026-02-28.md](../Gemini/ThumbnailLogic_2026-02-28.md)**
  - サムネイル生成の流れを技術的に追う入口です。
- **[ThumbnailEngineRouting_2026-03-01.md](../Gemini/ThumbnailEngineRouting_2026-03-01.md)**
  - エンジン切り替え基準を確認する入口です。
- **[Everything_to_Everything_Flow_Design_2026-02-28.md](../Gemini/Everything_to_Everything_Flow_Design_2026-02-28.md)**
  - Watcher / Everything 系のフロー設計です。
- **[FFmpeg_Guidelines.md](FFmpeg_Guidelines.md)**
  - FFmpeg 利用方針と関連調査の入口です。
- **[EmojiPathStatus_2026-03-01.md](../Gemini/EmojiPathStatus_2026-03-01.md)**
  - 絵文字パス問題の現状整理です。
- **[動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md](../Gemini/動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md)**
  - 並列制御まわりの設計判断を追う資料です。
- **[ライブラリ比較_変換速度ベンチ結果_2026-02-25.md](../Gemini/ライブラリ比較_変換速度ベンチ結果_2026-02-25.md)**
  - ベンチ結果から採用判断を確認する資料です。

## 文書の見分け方

- `AI向け_作業指示_*.md`
  - 実装担当に渡すスコープと禁止線です。
- `AI向け_レビュー指示_*.md`
  - レビュー担当に渡す観点です。
- `AI向け_レビュー結果_*.md`
  - 受け入れ判断と残課題の記録です。
- `Implementation Plan_*.md`
  - 実装計画と段取りです。
- `調査結果_*.md`
  - 事象の切り分けと原因分析です。
- `_初版.md`
  - 初期メモです。通常は日付付きの新版を優先します。

## 他フォルダへの入口

- 人が全体像を掴む資料を見る
  - **[../forHuman/README.md](../forHuman/README.md)**
- 背景説明や熱量高めの補助資料を見る
  - **[../Gemini/README.md](../Gemini/README.md)**
- Watcher 領域を見る
  - **[../../Watcher/README.md](../../Watcher/README.md)**
- Thumbnail 領域を見る
  - **[../../Thumbnail/README.md](../../Thumbnail/README.md)**
