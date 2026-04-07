# AI向け Thumbnail ドキュメント目次

更新日: 2026-04-01

このファイルは `Thumbnail/` 配下のドキュメント構造をAIが素早く把握するためのインデックスである。
人間向けの案内は [README.md](../../README.md) を参照すること。

## ディレクトリ構造

```
Thumbnail/
├── README.md                ← 人間向け入口（全資料リンク）
├── AI向け_Thumbnail目次.md  ← このファイル
├── AI向け_引き継ぎ_Thumbnail基盤整理と次着手_2026-03-20.md ← 直近の再開ポイント
├── Docs/                    ← アクティブな資料（28本）
├── Docs/history/            ← 完了済み・歴史的資料（29本）
├── 救済worker/              ← 救済exe専用の設計・運用資料
├── Test/                    ← テスト計画・回帰チェック
├── Engines/                 ← エンジン実装コード
└── *.cs                     ← サムネイル生成の本体コード
```

---

## 直近の再開資料

| ファイル | 種別 | 概要 |
|---------|------|------|
| AI向け_引き継ぎ_Thumbnail基盤整理と次着手_2026-03-20.md | 引き継ぎ | `ThumbnailCreationService` 基盤整理の到達点と次着手 |

---

## Docs/ — アクティブ資料（31本）

### 救済exe関連（グループ F: 9本）
| ファイル | 種別 | 概要 |
|---------|------|------|
| Implementation Plan_サムネイル救済処理_ERROR動画一括救済_2026-03-12.md | 計画 | ERROR動画一括救済の実装計画 |
| Implementation Plan_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md | 計画 | 本exe高速化＋救済exe分離の実装計画（Phase 1-5） |
| Review_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md | レビュー | 上記計画の全体レビュー |
| Review_Phase1_FailureDb最小土台_2026-03-14.md | レビュー | Phase 1 コードレビュー |
| Review_Phase2前半_救済exe最小導入_2026-03-14.md | レビュー | Phase 2 前半レビュー |
| Review_Phase2後半_retry縮退_rescueLaneOFF_DLLセッション_2026-03-14.md | レビュー | Phase 2 後半レビュー |
| Review_Phase3_rescued同期_handoff削除_lane戻し_2026-03-14.md | レビュー | Phase 3 レビュー |
| 救済レーン実動画確認チェックリスト_2026-03-12.md | 運用 | 実動画確認のチェック観点 |
| 救済レーン再確認手順_エンジン強制解除_2026-03-13.md | 運用 | エンジン強制解除の確認手順 |

### 管理者権限サービス（グループ E: 3本）
| ファイル | 種別 | 概要 |
|---------|------|------|
| 設計メモ_管理者権限サービス責務境界_2026-03-07.md | 設計 | 責務境界の整理 |
| 設計メモ_管理者権限テレメトリ劣化ログ方針_2026-03-07.md | 設計 | テレメトリ方針 |
| 設計メモ_共通管理者権限サービス基盤方針_2026-03-07.md | 設計 | 基盤方針 |

### 設計・調査
| ファイル | 種別 | 概要 |
|---------|------|------|
| AI向け_サムネ成功後メインタブ再読込ガイド_2026-03-30.md | AI向け | サムネ成功後の main tab 再読込の正本 |
| AI向け_未作成走査ボタン動作_2026-03-30.md | AI向け | `未作成走査` ボタンの処理と通常キュー投入の整理 |
| 設計メモ_engine-client責務表_Public本体責務集中_2026-04-04.md | 設計 | Public repo 側 `engine-client` の責務を app 中心に固定 |
| 調査結果_同名画像優先サムネ表示_2026-04-01.md | 調査 | WhiteBrowser互換の同名画像優先表示要望を、表示優先/生成スキップに分けて整理 |
| DCO_エンジン分離実装規則_2026-03-05.md | 規則 | エンジン分離時の実装規則 |
| DEC_サムネイル並列レーン閾値プリセット方針_2026-03-05.md | 方針 | 並列レーン閾値の決定方針 |
| Flowchart_動画情報取得_サムネイル作成_ハッシュ作成タイミング_2026-03-04.md | 図 | 処理フロー全体図 |
| 設計メモ_FileIndexProvider異常とサムネイル高負荷ログ分離_2026-03-07.md | 設計 | 高負荷ログ分離の設計 |
| 設計メモ_WhiteBrowser既定thum互換_2026-03-15.md | 設計 | WB互換のサムネパス設計 |
| 実測調整メモ_サムネイル高負荷係数と閾値_2026-03-07.md | 調整 | 高負荷係数の実測結果 |
| plan_DRM保護_非対応コーデック_代替サムネイル実装_2026-03-01.md | 計画 | DRM/未対応コーデック対応 |
| 調査結果_AutogenNativeCrash_最重要ポイント_2026-03-14.md | 調査 | AutogenネイティブCrash対策 |
| 調査結果_低速Thread現状まとめ_2026-03-18.md | 調査 | BigMovie（旧低速Thread）の現状整理 |
| Implementation Plan_プレースホルダ追加_NoData_AppleDouble_Flash_2026-03-20.md | 計画 | NoData / AppleDouble / Flash の placeholder 追加 |
| 調査結果_slowバックグラウンド時カーソル引っかかり原因と対策案_2026-03-07.md | 調査 | UIカーソル引っかかり調査 |
| 調査結果_サムネエンジン比較_fork大粒度アーキ_リペア処理_並列管理の移植観点_2026-03-11.md | 調査 | fork版アーキ比較 |

### 現状把握・運用
| ファイル | 種別 | 概要 |
|---------|------|------|
| 現状把握_workthree_失敗動画検証と本線反映方針_2026-03-11.md | 現状 | workthree観点の失敗動画整理 |
| 優先順位表_失敗9件の検証順_2026-04-07.md | 計画 | 検証順の優先順位 |
| 手動再試行運用手順.md | 運用 | Failed→Pending 再試行手順 |
| サムネイルが作成できない動画対策.md | 運用 | 失敗動画の対処法 |

---

## Docs/history/ — 歴史的資料（29本）

### 統合候補グループ

**A: 絵文字パス対策（4本）** — 完了済み
- EmojiPathMitigation_絵文字問題 症状と対策.md
- EmojiPathMitigationDetailDesign.md
- EmojiPathStatus_2026-03-01.md
- LibrarySurvey_絵文字パス代替ライブラリ調査_2026-02-25.md

**B: エンジン分離 Phase 1-4（9本）** — 完了済み
- Implementation Plan_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md
- Implementation Plan_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md
- Implementation Plan_Phase3_キュー進捗通知インターフェース化_2026-03-04.md
- Implementation Plan_Phase4_Rust外出し準備_QueueDBハッシュ保持_マイグレーションなし_2026-03-04.md
- Implementation Plan_サムネイル作成エンジン別プロジェクト化_2026-03-03.md
- tasklist_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md
- tasklist_Phase3_キュー進捗通知インターフェース化_2026-03-04.md
- ManualRegressionCheck_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md
- ManualRegressionCheck_Phase2_サムネイルキュー別プロジェクト化_2026-03-04.md

**C: 進捗パネルUI（3本）** — 完了済み
- Implementation Plan_サムネイル進捗タブミニスレッドパネル表示レスポンス追加改善_2026-03-05.md
- Implementation Plan_サムネイル進捗ミニパネルWriteableBitmapプレビュー直結_実装計画兼タスクリスト_2026-03-04.md
- Implementation Plan_サムネイル進捗ミニパネル表示遅延対策_2026-03-04.md

**D: FFMediaToolkit切替（3本）** — 完了済み
- Implementation Plan_MovieInfo_FFMediaToolkit切替.md
- MovieInfo_FFMediaToolkit切替影響範囲とベンチ_2026-02-25.md
- MovieInfo情報取得ベンチ_FFMediaToolkit_vs_SinkuDLL_2026-02-25.md

**G: ベンチマーク（2本）** — 参考資料
- Hash取得ベンチ結果_2026-02-25.md
- ライブラリ比較_変換速度ベンチ結果_2026-02-25.md

**H: QueueDBアーキテクチャ（3本）** — 完了済み
- plan_AsyncQueueDbArchitecture_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md
- tasklist_QueueDb_列整理とDone保守削除_2026-02-28.md
- Implementation Plan.md

### その他
- Implementation Plan_サムネイル並列レーン化と大動画低優先制御_実装計画兼タスクリスト_2026-03-05.md
- Implementation Plan_監視フォルダ追加スキャンDB登録高速化.md
- FfmpegAutoGenThumbnailGenerationEngine_修正プラン_gemini_plan_2026-02-28.md
- 動的並列_ジョブ優先度とスレッド優先度の違い_2026-03-05.md
- 調査結果_監視フォルダ追加スキャンDB登録ボトルネック_2026-02-24.md

---

## 救済worker/ — 救済exe専用資料

- 伝達書_救済worker_Debug実行切り分け_2026-03-15.md
- 中期計画_救済exe段階改善_2026-03-15.md
- 設計メモ_救済exe処理順とFailureDb書込アルゴ再考_2026-03-15.md
- 未解決束レポート_p6_2026-03-15.md

---

## Test/ — テスト関連

- README.md（テスト入口）
- plan_TestAsyncQueue_サムネイルキュー_テスト戦略.md

---

## 参照の起点

- **人間向け**: [README.md](../../README.md)
- **基本設計**: [../Docs/ThumbnailLogic_2026-02-28.md](../../Docs/Gemini/ThumbnailLogic_2026-02-28.md)
- **workthree ブランチ方針**: [../AI向け_ブランチ方針_ユーザー体感テンポ最優先_2026-04-07.md](../../AI向け_ブランチ方針_ユーザー体感テンポ最優先_2026-04-07.md)
- **大機能理解書**: [../Docs/AI向け_大機能詳細理解書_2026-03-07.md](../../Docs/AI向け_大機能詳細理解書_2026-03-07.md)
