# 🏎️ 1パネルベンチマーク結果 — エンジン×HWアクセラレーション公平比較

> **実行日: 2026-03-01** | 対象: 絵文字あり小ファイル 9本 | 1パネル生成 | warmup=1

## 条件

- **対象フォルダ**: 絵文字あり小ファイルたち（9動画）
- **ベンチ方式**: 3エンジン × 2モード(none, cuda) × warmup1回 + 本番1回ローテーション
- **1パネル生成**（`Tabindex=0` 相当）

## 結果 🔥

| モード | エンジン | 実行数 | 成功 | 失敗 | 平均ms | 最速ms | 最遅ms |
|:------:|---------|------:|-----:|-----:|-------:|-------:|-------:|
| **none** | autogen | 9 | 9 | 0 | **110** | 11 | 243 |
| **none** | ffmediatoolkit | 9 | 9 | 0 | 226 | 40 | 344 |
| **none** | ffmpeg1pass | 9 | 9 | 0 | 342 | 203 | 471 |
| **cuda** | autogen | 9 | 9 | 0 | **128** | 17 | 263 |
| **cuda** | ffmediatoolkit | 9 | 9 | 0 | 225 | 29 | 333 |
| **cuda** | ffmpeg1pass | 9 | 9 | 0 | 706 | 413 | 858 |

## 分析 💡

### autogen が最速！
- none: **110ms** / cuda: **128ms** — どちらも圧倒的最速
- FFMediaToolkitの約半分、ffmpeg1passの1/3以下

### CUDAの効果は？
- **autogen**: none(110ms) vs cuda(128ms) → CUDAの方がやや遅い（小ファイルではオーバーヘッドが目立つ）
- **ffmediatoolkit**: ほぼ同等（225ms vs 226ms）
- **ffmpeg1pass**: none(342ms) vs cuda(706ms) → **CUDAの方が2倍遅い！** CLIプロセス起動＋GPU転送コストが支配的

### 結論
- 小ファイル1パネル生成では **autogen + none（CPU）が最強** 🏆
- CUDA は小ファイルではオーバーヘッドが利点を上回る（大ファイル・多パネルで真価を発揮する想定）
- ffmpeg1pass(CLI) は毎回プロセス起動コストがかかるため1パネル生成には不向き

## 元データ

- `thumbnail-engine-bench-folder-small-fair-combined_20260301_141220.csv` は現在このリポジトリ内に存在しない
- `thumbnail-engine-bench-folder-small-fair-summary_20260301_141220.csv` は現在このリポジトリ内に存在しない
