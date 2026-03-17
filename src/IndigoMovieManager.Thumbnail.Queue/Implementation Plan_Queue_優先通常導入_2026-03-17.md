# Implementation Plan_Queue_優先通常導入_2026-03-17

最終更新日: 2026-03-17

変更概要:
- サムネイルQueueへ明示Priorityを `優先 / 通常` の2段階で導入する計画を整理
- 既存の `current tab / visible / near-visible / slow lane` と衝突しない順序を定義
- `Realtime` や preempt は今回の対象外とし、体感テンポを壊さない最小導入へ絞る
- `QueueObj` 契約計画書との同期漏れを解消し、実装済みタスクの状態を文書へ反映する

## 1. 目的

- 本計画の目的は、Queueへ「明示Priority」を最小構成で入れることにある。
- ただし、複雑な多段階制御は入れない。
- 今回は `優先 / 通常` の2値だけを持ち、ユーザー操作起点の小さな要求を後回しにしないことを主目的とする。
- 最上位の判断基準は `workthree` 方針どおり、通常動画の体感テンポを壊さないこととする。

## 2. 今回の結論

- 導入するPriorityは `優先` と `通常` の2段階だけにする。
- `Realtime` は導入しない。
- `manual capture` は今も Queue を通らず `CreateThumbAsync(..., true)` 直実行なので、今回の対象外とする。
- Queue内の処理順は、lease段階と実行段階を分けて次の順序に固定する。
  1. `Priority`
  2. 現在タブ優先
  3. visible -> near-visible 順
  4. 同一 `Priority` + 同一可視bucket 内だけ `Normal lane -> Slow lane`
  5. 同一bucket + 同一lane 内は `CreatedAtUtc ASC`
- 既に実行を開始した `通常` ジョブは追い越さない。
- ただし、まだ未着手の lease バッファに積まれている `通常` ジョブに対しては、後着の `優先` ジョブを前へ差し込める構造を導入対象にする。

## 3. いま問題になっている点

- 現状のQueueには `Priority` 列がない。
- その代わり、lease時に `current tab` と `visible movie path keys` を使って先頭候補を寄せている。
- さらに lease後に `SortLeasedItemsByLane` が走るため、SQLで先に取った順と実行順が完全一致するわけではない。
- このため、Priorityを入れるなら「DBの取得順」と「lease後の並べ替え」の両方を更新しないと意味が薄い。

## 4. 今回採る設計

### 4.1 Priority定義

- `通常 = 0`
- `優先 = 1`

名前は仮に `ThumbnailQueuePriority` とする。

### 4.2 Priorityの付与方針

- `優先`
  - UI起点の単発・少量要求
  - 詳細サムネイルの不足補完
  - 現在選択中動画の明示再生成
  - 現在タブでの明示操作から来る少量要求
- `通常`
  - Watcher起点
  - 監視フォルダの再走査由来
  - 一括再作成
  - 欠損補完のバッチ投入

### 4.3 同一ジョブの扱い

- 同一キーは従来どおり `MainDbPathHash + MoviePathKey + TabIndex` を一意とする。
- Priorityは一意キーには含めない。
- 既存行が `通常` で、新規要求が `優先` なら昇格する。
- 既存行が `優先` で、新規要求が `通常` でも降格しない。
- `Processing` 中の行は現行どおり保護し、途中昇格はしない。

## 5. 今回やらないこと

- `Realtime` の導入
- 実行中ジョブの preempt
- `Processing` 中ジョブのPriority昇格
- 救済workerへのPriority伝搬
- 設定画面でのPriorityカスタマイズ
- 3段階以上のPriority化

## 6. 影響範囲

### 6.1 Contracts / DTO

- `src/IndigoMovieManager.Thumbnail.Contracts/QueueObj.cs`
  - Priorityプロパティ追加
  - 共有DTO変更に合わせて関連する Contracts 計画書も同時更新する
- `src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/QueueRequest.cs`
  - QueueObjからPriorityを引き継ぐ
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - Upsert用DTO / Lease用DTOへPriority追加
  - lease後の整列で使う一時属性として `LeaseBucketRank` / `LeaseOrder` を持てる形にする

### 6.2 App投入経路

- `Thumbnail/MainWindow.ThumbnailQueue.cs`
  - `TryEnqueueThumbnailJob` へ Priority 指定を通す
  - debounce と priority昇格の整合を取る
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - Queue consumer 起動自体は既存のまま
- `BottomTabs/Extension/MainWindow.BottomTab.Extension.DetailThumbnail.cs`
  - 詳細サムネ補完を `優先` 候補として扱う
- `Views/Main/MainWindow.MenuActions.cs`
  - 明示再作成の投入Priorityを定義する
- `Watcher/MainWindow.Watcher.cs`
  - Watcher由来は `通常` で固定する

### 6.3 QueueDB / lease順

- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbSchema.cs`
  - `Priority INTEGER NOT NULL DEFAULT 0` を追加
  - 後方互換の列追加処理を足す
  - 取得順に効く index を見直す
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - Upsertで昇格ルールを実装
  - `GetPendingAndLease` の `ORDER BY` へ Priority を追加
  - 必要に応じて `priority only` 小口lease、または同等の軽量判定APIを追加する

### 6.4 lease後の実行順 / バッファ

- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - `SortLeasedItemsByLane` を Priority aware にする
  - `Priority` と `LeaseBucketRank` を保ったまま、同一bucket内だけ lane で並べ替える
  - `EnumerateLeasedItemsAsync` の未着手bufferへ、後着の `優先` ジョブを前挿しできるようにする
  - 既に着手済みジョブの preempt は行わない

### 6.5 テスト

- `Tests/IndigoMovieManager_fork.Tests/QueueDbVisiblePriorityTests.cs`
  - visible優先とPriorityの共存テストを追加
- `Tests/IndigoMovieManager_fork.Tests/QueueDbPathResolverTests.cs`
  - 旧schemaからの列追加確認を更新
- `Thumbnail/Test/TestMockServices.cs`
  - テスト用schemaへ Priority 列追加
- 旧テスト資産の `QueueDbServiceTests` / `ThumbnailQueuePersisterTests` / `ThumbnailQueueProcessorTests`
  - schema差分の追従が必要

### 6.6 関連ドキュメント

- `src/IndigoMovieManager.Thumbnail.Contracts/Implementation Plan_Contracts候補_QueueObj切り出し_2026-03-17.md`
  - `QueueObj` へ Priority を追加する前提へ更新する
  - 共有DTOの責務拡張理由と非対象範囲を追記する

## 7. 順序ルールの確定

今回の最重要ルールはここで固定する。

### 7.1 DB lease順

`GetPendingAndLease` の SQL は、概念的に次の順へ変更する。

1. `Priority DESC`
2. `current tab` 一致
3. `preferred visible movie path keys` の並び順
4. `CreatedAtUtc ASC`

### 7.2 lease後の並べ替え

`SortLeasedItemsByLane` は次の順へ変更する。

1. `Priority DESC`
2. `LeaseBucketRank ASC`
3. `lane rank ASC` (`Normal` 先、`Slow` 後)
4. `MovieSizeBytes ASC`
5. `LeaseOrder ASC`

ここでいう `LeaseBucketRank` は、`current tab / visible / near-visible / other` のまとまりを崩さないための in-memory 用順位である。

これにより、`優先` が `slow lane` だからといって `通常` の `normal lane` より後ろへ落ちることを防ぎつつ、同一Priority内の可視bucketを保てるようにする。

### 7.3 未着手leaseバッファの扱い

- 現行processorは lease件数を先取りし、buffer が空になるまで次の lease を取りに行かない。
- このままだと、SQLへ Priority を入れても「既にbufferへ積まれた通常ジョブ」が `優先` を待たせる。
- したがって初版では、次の制御を導入対象にする。
  - 既に着手済みのジョブはそのまま継続する
  - 未着手の通常bufferが残っている間でも、DBに `優先` Pending が現れたら小口leaseを取り、未着手通常bufferの前へ差し込む
  - 通常系の大量先取りは維持しつつ、`優先` だけ小口で追いつける形にする
- これにより preempt を入れずに、`優先` の体感待ち時間だけを縮める。

## 8. debounce / dedupe の扱い

現行は 800ms の debounce が先に走るため、次の問題がある。

- 直前に `通常` で入った同一キーへ、すぐ `優先` を投げても握りつぶされる可能性がある

今回の方針は次のとおり。

- 同一キーへの `優先` 要求は debounce で落とさない
- ただし Channel 膨張防止の思想は維持する
- Persister の dedupe では「最後の要求」ではなく「最も強いPriorityの要求」を残す
- `通常` が buffer 済みでも、`優先` の後着要求は未着手分へ割り込める余地を残す

## 9. UI方針

- 初版では Queueタブや進捗タブに Priority 表示は出さない
- まずは処理順の改善だけを入れる
- 見た目の変更は最小限にする
- 必要になった時だけ、後続でログや進捗表示へ `優先` ラベルを追加する

## 10. Phase分割

### Phase A: 型追加と付与元整理

- `QueueObj` / `QueueRequest` / DB DTOへ Priority を追加
- 投入元ごとの `優先 / 通常` ルールを決めて埋める
- manual capture は対象外として明記する
- `QueueObj` 契約変更に合わせて Contracts 側の計画書も更新する

### Phase B: QueueDB対応

- schemaへ `Priority` 列追加
- 既存DB向けの `ALTER TABLE` を追加
- Upsert昇格ルールを実装
- dedupeで強いPriorityを保持する

### Phase C: lease順とlane順の整理

- `GetPendingAndLease` の `ORDER BY` を更新
- `LeaseBucketRank` / `LeaseOrder` を使って `SortLeasedItemsByLane` を Priority aware に更新
- 未着手leaseバッファへ `優先` を前挿しできるようにする
- visible優先との共存テストを追加

### Phase D: 回帰確認

- Watcher大量投入時に通常動画テンポが落ちていないか確認
- UI起点の単発要求が先に完了するか確認
- 旧QueueDBから起動して migration が壊れないか確認
- 既着手ジョブを止めずに、未着手bufferだけへ `優先` が割り込めることを確認する

## 11. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| PRIO-001 | 完了 | Priority enum / DTO追加 | `QueueObj.cs`, `QueueRequest.cs`, `QueueDbService.cs` | Queue経路で Priority を保持できる |
| PRIO-002 | 完了 | 投入元ごとの Priority 付与 | `MainWindow.ThumbnailQueue.cs`, `MenuActions.cs`, `Watcher.cs`, `DetailThumbnail.cs` | `優先 / 通常` の入口が固定される |
| PRIO-003 | 完了 | QueueDB schema拡張 | `QueueDbSchema.cs` | 旧DBでも `Priority` 列が自動追加される |
| PRIO-004 | 完了 | Upsert昇格ルール実装 | `QueueDbService.cs` | `通常 -> 優先` は昇格し、逆は降格しない |
| PRIO-005 | 完了 | dedupeで強Priority保持 | `ThumbnailQueuePersister.cs` | 同一キーで最強Priorityが残る |
| PRIO-006 | 完了 | lease順へ Priority 追加 | `QueueDbService.cs` | `優先` が `通常` より先にleaseされる |
| PRIO-007 | 完了 | lease bucket保持つき lane sort 反映 | `ThumbnailQueueProcessor.cs` | `優先` と visible bucket を保ったまま lane順が効く |
| PRIO-008 | 完了 | 未着手leaseバッファへの優先前挿し | `ThumbnailQueueProcessor.cs`, `QueueDbService.cs` | 後着 `優先` が未着手通常bufferを追い越せる |
| PRIO-009 | 一部完了 | migration / 順序 / visible共存テスト追加 | `Tests/*`, `Thumbnail/Test/*` | `Tests/IndigoMovieManager_fork.Tests` 側は反映済み。`Thumbnail/Test/*` の旧仮実装群は別途整理が残る |
| PRIO-010 | 完了 | `QueueObj` 契約文書更新 | `Implementation Plan_Contracts候補_QueueObj切り出し_2026-03-17.md` | 共有DTO変更の説明が同期される |
| PRIO-011 | 未着手 | 実動画で体感確認 | `Thumbnail/*.md` または確認メモ | UI起点の単発要求が後回しにならない |

## 12. テスト観点

- 同一キーへ `通常` の後で `優先` を入れると、最終Priorityが `優先` になる
- 同一キーへ `優先` の後で `通常` を入れても降格しない
- `current tab / visible` 優先は、同Priority内で従来どおり効く
- `優先 + slow lane` が `通常 + normal lane` より後ろへ落ちない
- 同一Priority内では `LeaseBucketRank` をまたいで lane sort が順序を壊さない
- Watcher大量投入の中で、UI起点の `優先` が先に完了する
- 旧QueueDBからの起動で `Priority` 列が追加される
- `Processing` 中ジョブへ再投入しても既存保護が壊れない
- 後着の `優先` が、未着手の通常bufferだけを追い越せる
- 既に着手済みの通常ジョブは中断されない

## 13. リスクと対策

- リスク: `visible` 優先と `Priority` 優先の責務が曖昧になる
  - 対策: `Priority` を先、`visible` を次と明文化する
- リスク: lease後の lane sort で Priority の意味が薄れる
  - 対策: `LeaseBucketRank` と `LeaseOrder` を持たせ、同一bucket内だけ lane sort を効かせる
- リスク: debounce で `優先` 昇格要求が落ちる
  - 対策: `優先` は debounce bypass か、少なくとも昇格要求だけは落とさない
- リスク: `優先` 改善をSQL順だけで終えると、既存leaseバッファの通常ジョブを追い越せない
  - 対策: 未着手bufferへの前挿しを初版の対象へ含める
- リスク: schema変更で旧テスト資産が割れる
  - 対策: 本体テストと旧 `Thumbnail/Test` 両方の schema を同時更新する
- リスク: `QueueObj` 変更で Contracts 文書だけ旧前提のまま残る
  - 対策: 文書更新を同一タスク・同一コミット系列へ含める

## 14. 採否基準

今回のPriority導入は、次を満たした時だけ採用とする。

- Watcher主体の通常運用で体感テンポを悪化させない
- UI起点の単発要求が後回しになりにくくなる
- Queueの複雑さが `Realtime` や preempt まで増えない
- 既存の `visible / near-visible / slow lane` の思想を壊さない

## 15. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Contracts\QueueObj.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueuePipeline\QueueRequest.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueuePipeline\ThumbnailQueuePersister.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbSchema.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailQueue.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\BottomTabs\Extension\MainWindow.BottomTab.Extension.DetailThumbnail.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Views\Main\MainWindow.MenuActions.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Watcher\MainWindow.Watcher.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\UpperTabs\Common\MainWindow.UpperTabs.Viewport.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\QueueDbVisiblePriorityTests.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager_fork.Tests\QueueDbPathResolverTests.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Test\TestMockServices.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Contracts\Implementation Plan_Contracts候補_QueueObj切り出し_2026-03-17.md`
