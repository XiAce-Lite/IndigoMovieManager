# Implementation Plan ThumbnailQueueProcessor progress_parallel切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `DynamicParallelGate` を独立型へ昇格した
- `ThumbnailParallelLimitMonitor` を追加し、並列上限の周期追従を `QueueProcessor` から外した
- `ThumbnailQueueProgressPublisher` を追加し、進捗表示と job callback を `QueueProcessor` から外した
- `ThumbnailLeaseCoordinator` を追加し、lease 列挙と継ぎ足しを `QueueProcessor` から外した
- `ThumbnailLeaseHeartbeatRunner` を追加し、lease heartbeat を `QueueProcessor` から外した
- `ThumbnailQueueBatchState` を追加し、session / batch の件数管理を `QueueProcessor` から外した
- `ThumbnailQueueBatchRunner` を追加し、1 batch 実行本体を `QueueProcessor` から外した
- `ThumbnailQueuePerfLogger` を追加し、perf summary と累計計測を `QueueProcessor` から外した
- `ThumbnailQueueProcessor` は gate / progress / lease / batch 状態の詳細を持たず、委譲だけにした

## 1. 目的

`ThumbnailQueueProcessor` に残っていた

- dynamic parallel gate の内部状態管理
- 設定変更追従 monitor
- 進捗表示 handle の open / report / close
- job started / completed callback
- lease 列挙と継ぎ足し
- lease heartbeat
- session / batch 件数管理
- batch 実行本体
- perf summary と累計計測

を外へ出し、`RunAsync` を進行制御へさらに寄せる。

## 2. 今回やったこと

1. `DynamicParallelGate` を独立ファイルへ移した
2. `ThumbnailParallelLimitMonitor` を追加した
3. `ThumbnailQueueProgressPublisher` を追加した
4. `ThumbnailLeaseCoordinator` を追加した
5. `ThumbnailLeaseHeartbeatRunner` を追加した
6. `ThumbnailQueueBatchState` を追加した
7. `ThumbnailQueueBatchRunner` を追加した
8. `ThumbnailQueuePerfLogger` を追加した
9. `ThumbnailQueueProcessor` の progress / parallel / lease / heartbeat helper を削った
10. `ThumbnailQueueProcessor` の batch 件数管理と batch 実行本体を専用型へ寄せた
11. `ThumbnailQueueProcessor` の perf summary と累計計測を専用型へ寄せた

## 3. 判断

- `RunAsync` の中で gate の内部実装まで持つ必要はない
- progress handle の lock 管理と callback 保護は、進捗専用クラスへ寄せた方が責務が読みやすい
- parallel monitor は gate 更新専用なので、processor 本体から分ける価値がある
- lease 列挙は buffer / preferred probe / 継ぎ足しのまとまりとして独立させた方が読みやすい
- heartbeat は長時間処理保護だけの責務なので、processor 本体から切る方が明快
- session / batch 件数は共有 state に寄せた方が `RunAsync` の見通しが良い
- batch 実行本体は worker 実行と後処理をまとめて持つ方が責務境界が自然
- perf summary は file log と累計値を持つので、processor から切る方が副作用位置を固定できる

## 4. 今回やらないこと

1. outer loop の wait / reacquire 判定整理
2. `ResolveConfiguredParallelism` / `ResolveLeaseBatchSize` の設定吸収整理
3. queue batch 単位の coordinator 追加

## 5. 次の候補

1. `ThumbnailQueueProcessor` の `while` 二重ループを段階分離する
2. outer loop の queue empty / drain 判定を専用型へ寄せる
3. A5 へ進み、`ThumbnailCreationService` 側の orchestration 分割へ入る
