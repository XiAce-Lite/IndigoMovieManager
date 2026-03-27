# Implementation Plan ThumbnailQueueProcessor failure切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `ThumbnailFailureRecorder` を追加し、Queue失敗時の状態更新と FailureDb 追記を切り出した
- `ThumbnailQueueProcessor` は failure 記録の詳細を持たず、委譲だけにした
- `ThumbnailFailureDbTests` は reflection ではなく `ThumbnailFailureRecorder` を直接呼ぶ形へ寄せた

## 1. 目的

`ThumbnailQueueProcessor` に混在していた

- QueueDB の失敗状態更新
- FailureDb への terminal failure 親行追記
- failure kind / lane / extra json 判定

を `RunAsync` から外へ出し、consumer 本体を進行制御寄りへ寄せる。

今回は Phase A4 の 1 手目として、failure 経路だけを切り出す。

## 2. 今回やったこと

1. `ThumbnailFailureRecorder` を追加した
2. `HandleFailedItem` の中身を `ThumbnailFailureRecorder.HandleFailedItem` へ移した
3. FailureDb append と failure kind / lane 判定も同じクラスへ集約した
4. `ThumbnailQueueProcessor` から failure 専用 helper と cache を削った
5. `ThumbnailFailureDbTests` は新クラスを直接叩く形へ更新した

## 3. 判断

- failure 経路は QueueDB と FailureDb の両方を触るため、`RunAsync` に残すと責務が濁る
- ここを先に外すと、次の `lease` / `preferred probe` 分離でも `RunAsync` が読みやすくなる
- reflection ベースのテストより、切り出し先クラスを直接検証する方が今後の分割に強い

## 4. 今回やらないこと

1. `lease` 取得の別クラス化
2. preferred probe の別クラス化
3. lane scheduler / gate 制御の別クラス化

## 5. 次の候補

1. `AcquireLeasedItems` と preferred probe を `ThumbnailLeaseAcquirer` / `ThumbnailLeaseBuffer` 相当へ分離する
2. `DynamicParallelGate` を `ThumbnailQueueProcessor` から独立型へ昇格する
3. progress 通知を `ThumbnailQueueProgressPublisher` 相当へ寄せる
