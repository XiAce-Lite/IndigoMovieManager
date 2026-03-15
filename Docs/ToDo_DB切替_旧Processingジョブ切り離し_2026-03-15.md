# ToDo: DB切替時の旧Processingジョブ切り離し (2026-03-15)

## 1. 位置づけ

- MainDB 切替時に、旧DB向け `Pending` はすでに捨てる実装が入っている。
- ただし、切替またぎで既に `Processing` に入っている旧ジョブは、現状そのまま完走させる。
- 2026-03-15 の体感テストでは許容だったため未対応のままにしているが、後で必要になった時にすぐ着手できるよう独立 ToDo として切り出す。

## 2. 目的

- DB切替直後に、旧DB向けの走行中ジョブが新DB体験へ遅着弾しないようにする。
- 「止める」「戻す」「捨てる」の境界を明示し、切替またぎの責務を曖昧にしない。

## 3. 現状の問題

- `CheckThumbAsync(...)` の consumer はアプリ寿命で常駐し、DB切替では参照先だけを切り替えている。
- そのため、旧DBで lease 済みのジョブは、切替後も `CreateThumbAsync(...)` まで進み得る。
- 完了後の UI / DB 反映は現在の `MainVM.DbInfo` を見ているため、将来的に違和感や誤反映の温床になり得る。

## 4. 実装方針

### 4.1 cancel / restart

- DB切替時に通常 consumer を一度 `Cancel` し、切替後に再起動する。
- 目的は、旧DBの新規 lease 取得と後続処理をできるだけ早く止めること。

### 4.2 old QueueDB の `Processing` 巻き戻し API

- 旧DBの `.queue.imm` に対して、`OwnerInstanceId` 単位で `Processing` を `Pending` か `Failed` に戻す API を追加する。
- 少なくとも以下を満たす必要がある。
  - 対象は旧DBだけ
  - 対象 owner は現在本体インスタンスだけ
  - `OwnerInstanceId` / `LeaseUntilUtc` をクリアする
  - ログで何件戻したか追える

### 4.3 完了後の stale session guard

- `CreateThumbAsync(...)` 完了後の UI / DB 反映に、`MainDbSessionStamp` ベースの stale 判定を追加する。
- 旧セッションのジョブだった場合は、以下を捨てる。
  - 一覧側サムネパス反映
  - movie テーブル更新
  - 進捗 UI への完了反映

### 4.4 テスト

- 追加対象の主な確認観点
  - 切替時に consumer が cancel / restart される
  - 旧DBの `Processing` が owner 単位で巻き戻る
  - 新DBへ切替後、旧セッション完了結果が UI / DB に反映されない
  - 同一DB再オープンでは誤発火しない

## 5. 変更候補ファイル

- `MainWindow.DbSwitch.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
- `Tests/IndigoMovieManager_fork.Tests/MainDbSwitchPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/*Queue*Tests.cs`

## 6. 方式の比較

- `Pending` に戻す案
  - 途中中断後も後で再処理しやすい
  - 体感重視の本線には合いやすい
- `Failed` に落とす案
  - 切替後の残件が明確
  - ただし「失敗ではなく中断」を `Failed` へ混ぜる設計判断が必要

現時点の第一候補は `Pending` に戻す案。

## 7. 見積もり

- 実装コストは `中`
- 目安は `半日から1日`

## 8. 完了条件

- DB切替時に旧DB向け `Processing` が user 体感上残らない
- 旧セッション完了結果が新DBへ誤反映されない
- ログだけで `cancel -> 巻き戻し -> 再開` の流れを追える
- 切替失敗時は旧セッションを壊さない
