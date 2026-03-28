# Implementation Plan: 進捗プリセットで ffmpeg1pass エコ運転連動（2026-03-19）

## 1. 目的

- 既にユーザーが触っている `低速 / 普通 / 高速` の運用導線へ、`ffmpeg1pass` のエコ運転を自然につなぐ。
- 新しい設定項目を増やしすぎず、既存の並列数と進捗タブプリセットだけで `ffmpeg` 側の負荷も落とす。

## 2. 今回の方針

- `BottomTabs\ThumbnailProgress\MainWindow.BottomTab.ThumbnailProgress.cs`
  - 現在の `ThumbnailParallelism` / `ThumbnailSlowLaneMinGb` から、`ffmpeg1pass` 用のエコヒントを解決する。
- `Thumbnail\ThumbnailEnvConfig.cs`
  - `IMM_THUMB_FFMPEG1PASS_THREADS`
  - `IMM_THUMB_FFMPEG1PASS_PRIORITY`
  の反映窓口を一元化する。

## 3. ルール

- `低速`
  - `threads=1`
  - `priority=idle`
- `普通`
  - `threads=2`
  - `priority=below_normal`
- `高速`
  - ffmpeg 既定へ戻す

- 追補:
  - 実ジョブが `巨大動画で slow lane` に入った時は、プリセットに関係なく `threads=1 / priority=idle` を優先する。
  - つまり `高速` プリセット中でも、slow lane 個体だけは強エコで回す。

補足:

- Custom 値でも、並列数をかなり落としている時はエコ寄りへ倒す。
  - 並列 `2` 以下: `1 / idle`
  - 並列 `4` 以下: `2 / below_normal`
  - それ以外: 既定

## 4. 反映タイミング

- アプリ起動時
- 共通設定を閉じた時
- 進捗タブで並列数を変えた時
- 進捗タブで巨大動画閾値を変えた時
- 進捗タブでプリセットを切り替えた時

## 5. テスト

- `Tests\IndigoMovieManager_fork.Tests\ThumbnailProgressEcoModeTests.cs`
  - `低速`
  - `普通`
  - `高速`
  - Custom fallback
