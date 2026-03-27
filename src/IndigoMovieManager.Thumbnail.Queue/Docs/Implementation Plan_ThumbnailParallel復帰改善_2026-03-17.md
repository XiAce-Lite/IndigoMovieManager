# Implementation Plan_ThumbnailParallel復帰改善_2026-03-17

## 1. 目的

- 一度縮退した並列数が、backlog が残っていても復帰しにくい問題を軽くする
- 復帰条件を雑に緩めず、実際に需要があった時だけ戻るようにする

## 2. 今回の反映

- `ThumbnailParallelController`
  - 復帰需要を `queueActiveCount` の終端値だけでなく、バッチ中のピーク値でも判定する
  - 需要判定は `>` ではなく `>=` に変更し、閾値ちょうどでも復帰対象にする
  - scale-up ログに `active_end` と `demand_peak` を出して、後追いしやすくする
- `ThumbnailQueueProcessor`
  - `EvaluateNext(...)` へバッチ開始時の `activeCountAtBatchStart` を渡す

## 3. 期待する効果

- バッチ開始時には backlog が十分あったのに、終端で件数が減ったせいで復帰を逃すケースを減らせる
- 縮退後の低並列貼り付きが減り、ユーザー体感のテンポを戻しやすくなる

## 4. 境界

- 復帰ブロック時間や cooldown はそのまま残す
- 高負荷時の縮退条件も変えない
- つまり「危険時は下げる」動きは維持しつつ、「需要があるのに戻れない」だけを狙って直す
