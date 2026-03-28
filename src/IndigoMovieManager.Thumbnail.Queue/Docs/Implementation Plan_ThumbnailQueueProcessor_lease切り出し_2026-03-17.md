# Implementation Plan ThumbnailQueueProcessor lease切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `ThumbnailLeaseAcquirer` を追加し、lease 取得と lane 順整列を切り出した
- `ThumbnailLeaseBuffer` を追加し、buffer 追記と preferred 差し込みを切り出した
- `ThumbnailQueueProcessor` は lease 取得詳細を持たず、委譲だけにした
- `ThumbnailQueuePriorityProcessorTests` は reflection ではなく新クラスを直接呼ぶ形へ寄せた

## 1. 目的

`ThumbnailQueueProcessor` に混在していた

- preferred tab / movie path の解決
- QueueDB からの lease 取得
- lane 順整列
- 未着手 buffer への lease 追記
- preferred 差し込み

を分け、`RunAsync` を進行制御寄りへ寄せる。

今回は Phase A4 の 2 手目として、lease と buffer だけを切り出す。

## 2. 今回やったこと

1. `ThumbnailLeaseAcquirer` を追加した
2. `AcquireLeasedItems` と `SortLeasedItemsByLane` を移した
3. `ThumbnailLeaseBuffer` を追加した
4. `AppendLeaseItems` / `TryFrontInsertPreferredLeaseItems` / `ShouldProbePreferredLease` を移した
5. `ThumbnailQueueProcessor` は新クラスを呼ぶだけにした
6. `ThumbnailQueuePriorityProcessorTests` を新クラス直接呼びへ更新した

## 3. 判断

- lease 取得は QueueDB 依存が濃く、buffer 操作とは分離した方が読みやすい
- preferred probe の条件と差し込みは `RunAsync` 本体に置くより buffer 専用クラスへ寄せた方が責務が明確
- reflection ベースのテストより、切り出し先クラスを直接叩く方が次の分割でも壊れにくい

## 4. 今回やらないこと

1. `EnumerateLeasedItemsAsync` の別クラス化
2. `DynamicParallelGate` の独立型昇格
3. progress 通知の別クラス化

## 5. 次の候補

1. `EnumerateLeasedItemsAsync` を `ThumbnailLeaseBuffer` か coordinator へさらに寄せる
2. `DynamicParallelGate` と monitor を `ThumbnailLaneScheduler` 相当へ寄せる
3. progress 通知を `ThumbnailQueueProgressPublisher` 相当へ切り出す
