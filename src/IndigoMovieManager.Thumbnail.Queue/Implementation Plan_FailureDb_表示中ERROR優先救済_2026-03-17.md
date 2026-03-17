# Implementation Plan_FailureDb_表示中ERROR優先救済_2026-03-17

最終更新日: 2026-03-17

変更概要:
- 現在画面に見えている `ERROR` 画像だけを、救済経路で先に処理するための最小計画を整理
- `QueueDb` ではなく `FailureDb -> RescueWorker` 側へ `優先 / 通常` を導入する前提で整理
- `詳細表示 / 選択行 / viewport 内行 / 一括救済` の優先付与ルールを固定し、通常動画テンポを壊さない範囲へ絞る
- Phase A から Phase C までの実装を反映し、契約文書との同期漏れも解消した
- `優先` rescue 到着時は worker 起動待機にも反映し、未起動なら通常キュー稼働中でも開始判定を前へ出すように更新した
- 進捗タブの救済Workerカードへ `優先:固定/一時` と `開始:優先起動/アイドル待ち/即時` を表示し、起動意図を追えるように更新した

## 1. 目的

- 目的は、今ユーザーが見ている `ERROR` 画像を後回しにしないことにある。
- ただし、通常キューの速度や一覧操作のテンポは壊さない。
- そのため、通常サムネ作成の `QueueDb` へ新しい横槍を入れるのではなく、既存の救済経路である `FailureDb -> RescueWorker` 側だけを優先化する。
- 今回の対象は「現在画面表示分の `ERROR` 画像」であり、全件一括の底上げは主目的にしない。

## 2. 現状整理

- 上側タブで `errorSmall.jpg` などの placeholder を見つけた時は、`BottomTabs/Common/MainWindow.BottomTabs.Common.cs` から `TryEnqueueThumbnailDisplayErrorRescueJob(...)` を呼び、`FailureDb` に `pending_rescue` を積む。
- `サムネ失敗` タブの `選択救済` / `一括救済` も、`Watcher/MainWindow.ThumbnailFailedTab.cs` から同じ救済入口へ流している。
- 救済要求の実体は `Thumbnail/MainWindow.ThumbnailRescueLane.cs` で `ThumbnailFailureRecord` を append している。
- RescueWorker 側の取得順は `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs` の `GetPendingRescueAndLease(...)` で `UpdatedAtUtc ASC, FailureId ASC` 固定であり、明示Priorityはない。
- 既存の duplicate 判定は `HasOpenRescueRequest(...)` で終わっており、同一動画へ「後から見えたので優先へ上げる」という昇格ができない。
- `サムネ失敗` タブは「見えている間だけ再読込」までは入っているが、DataGrid の viewport 内行だけを拾う仕組みはまだない。

## 3. 今回の結論

- 導入先は `QueueDb` ではなく `FailureDb` とする。
- RescueWorker が読む主レコードへ `優先 / 通常` を持たせる。
- `現在画面表示分` の定義は次で固定する。
  1. 詳細パネルで今見えている `ERROR` 詳細サムネ
  2. 上側タブの visible range 内にある placeholder 行
  3. `サムネ失敗` タブで現在選択されている行
  4. `サムネ失敗` タブの viewport 内で今見えている行
- `一括救済` は全件を無条件で `優先` にしない。
- `選択行` と `現在詳細表示中` は強い `優先`、viewport 内の自動救済は短命 `優先` に分ける。

## 4. Priority モデル

### 4.1 値の定義

- `通常 = 0`
- `優先 = 1`

名称は既存の `ThumbnailQueuePriority` を再利用する案を第一候補とする。

### 4.2 FailureDb で追加する列

- `Priority INTEGER NOT NULL DEFAULT 0`
- `PriorityUntilUtc TEXT NOT NULL DEFAULT ''`

`PriorityUntilUtc` は、画面に見えたことによる短命 `優先` を時間で落とすために使う。

### 4.3 有効Priorityの解釈

- `Priority = 通常` の時は常に通常
- `Priority = 優先` かつ `PriorityUntilUtc = ''` の時は固定優先
- `Priority = 優先` かつ `PriorityUntilUtc > now` の時は一時優先
- `Priority = 優先` でも `PriorityUntilUtc <= now` になったら、lease順では通常扱いに落とす

これにより、スクロールしただけの大量昇格が長時間残ることを防ぐ。

## 5. 優先付与ルール

### 5.1 固定優先

- `detail-error-placeholder`
- `サムネ失敗` タブの `選択救済`
- 右クリックなど、明示的に単発救済した要求

これらは `Priority = 優先`、`PriorityUntilUtc = ''` とする。

### 5.2 一時優先

- 上側タブで現在 visible range 内にある placeholder
- `サムネ失敗` タブの viewport 内で今見えている行

これらは `Priority = 優先`、`PriorityUntilUtc = now + 45秒` を初期案とする。

### 5.3 通常

- `一括救済` で画面外にある残り
- 起動直後や periodic sync の副次的再救済
- 既存の backlog 救済

これらは `Priority = 通常` とする。

## 6. duplicate / 昇格ルール

### 6.1 現状の問題

- いまは `HasOpenRescueRequest(...)` が true なら append せず終了する。
- このため、画面外で `通常` として積まれた救済要求が、後から画面に見えても `優先` に上がらない。

### 6.2 変更方針

- `HasOpenRescueRequest(...)` だけで終わらせず、`TryPromoteOpenRescueRequest(...)` 相当の昇格APIを追加する。
- 対象は main record (`Lane in normal/slow`) のみとする。
- ルールは次で固定する。
  - `pending_rescue` の既存 `通常` は `優先` へ昇格する
  - `pending_rescue` の既存 `優先` は降格しない
  - `processing_rescue` は途中で preempt しない
  - `rescued` は再救済しない

### 6.3 昇格時に更新する項目

- `Priority`
- `PriorityUntilUtc`
- `UpdatedAtUtc`
- `ExtraJson` の source 情報

`UpdatedAtUtc` も更新して、同一Priority内では「いま見えた」要求を取りこぼしにくくする。

## 7. RescueWorker の lease順

`GetPendingRescueAndLease(...)` の取得順は、概念的に次へ変える。

1. 有効 `Priority DESC`
2. `UpdatedAtUtc ASC`
3. `FailureId ASC`

補足:

- `PriorityUntilUtc` が切れた `優先` は SQL 上で通常扱いに落とす
- 既着手 `processing_rescue` は止めない
- priority は rescue lane のみで完結させ、通常 `QueueDb` の lease順には影響させない

## 8. UI ごとの扱い

### 8.1 上側タブ

- 既存の `FilteredMovieRecs` 全体走査をそのまま優先化対象にしない
- `UpperTabVisibleRange` の visible / near-visible 情報を使い、現在見えている placeholder だけを一時優先で救済へ送る
- 上限件数は 8 から 16 件程度で打ち止めにし、タブ切替だけで大量投入しない

### 8.2 詳細パネル

- `detail-error-placeholder` は固定優先にする
- これは「今見ている1件」なので `requiresIdle = false` の即時起動候補として扱う

### 8.3 サムネ失敗タブ

- `選択救済` は固定優先
- `一括救済` は次の2段階に分ける
  - viewport 内で今見えている行: 一時優先
  - それ以外の表示行: 通常
- これにより、ユーザーの目の前の行から先に片付く形にする

### 8.4 viewport 行の取得

- `ThumbnailErrorTabView` の `DataGrid` から `ScrollViewer` と可視 `DataGridRow` を使って、今見えている `ThumbnailErrorRecordViewModel` 群を取る helper を追加する
- 上側タブの `UpperTabViewportTracker` と同じ思想で、見えている範囲だけを相手にする
- `SelectionChanged` や 1 秒ポーリングごとに全件昇格せず、表示変化時だけ差分昇格する

## 9. worker 起動方針

- 固定優先
  - `requiresIdle = false`
  - 理由: いま見ている1件・選んだ数件は待たせない方が体感が良い
- 一時優先
  - 既に worker が起動済みならそのまま拾わせる
  - rescue 要求自体は `requiresIdle = true` を維持する
  - ただし開始判定では `Priority = 優先` を見て、未起動 worker なら通常キュー稼働中でも起動可とする

制約:

- 既に起動済み worker の preempt は行わない
- 多重起動は増やさず、`1 worker = 1 movie` の前提を維持する

この方針で、目の前の単発要求だけでなく viewport 由来の `優先` も開始待ちを短縮しつつ、実行中ジョブの横取りは入れない。

## 10. 影響範囲

### 10.1 Contracts / DTO

- `src/IndigoMovieManager.Thumbnail.Contracts/QueueObj.cs`
  - 既存の `Priority` 2値を rescue 要求起点でも再利用する
  - 新しい共有プロパティは追加しない
- `src/IndigoMovieManager.Thumbnail.Contracts/Implementation Plan_Contracts候補_QueueObj切り出し_2026-03-17.md`
  - 既存の `QueueObj.Priority` 利用範囲説明と齟齬が出ないか確認する
  - 今回は新しい責務追加ではなく、既存 `Priority` の rescue 入口再利用に留める

### 10.2 FailureDb / RescueWorker

- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureRecord.cs`
  - `Priority` と `PriorityUntilUtc` 追加
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbSchema.cs`
  - 列追加と index 調整
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
  - append / promote / lease順 / 有効Priority判定を追加
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - lease結果の観測ログへ priority を出す程度の追従

### 10.3 App 側

- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - append 専用から「append または promote」へ変更
  - `launch_wait_policy` を ExtraJson へ残し、進捗タブが開始ポリシーを読めるようにする
- `BottomTabs/Common/MainWindow.BottomTabs.Common.cs`
  - visible range 内 placeholder だけを一時優先で投入
  - 詳細 placeholder は固定優先
- `Watcher/MainWindow.ThumbnailFailedTab.cs`
  - `選択救済` / `一括救済` / viewport 可視行の優先付与を実装
- `BottomTabs/ThumbnailError/MainWindow.BottomTab.ThumbnailError.Progress.cs`
  - Error タブ可視時だけ viewport 優先昇格を回す
 - `BottomTabs/ThumbnailProgress/MainWindow.BottomTab.ThumbnailProgress.cs`
  - 救済Workerカードの detail text へ優先度と開始ポリシーの観測表示を追加する
- `BottomTabs/ThumbnailError/ThumbnailErrorTabView.xaml.cs`
  - 今回は helper 追加なし
  - `Watcher/MainWindow.ThumbnailFailedTab.cs` 側で `DataGrid` の `ScrollViewer` と visible range を直接使う

## 11. 今回やらないこと

- 通常 `QueueDb` の並び順変更
- RescueWorker の多重起動制御思想の刷新
- `processing_rescue` 中ジョブの preempt
- 3段階以上のPriority化
- `サムネ失敗` タブ全件を常時自動優先化すること

## 12. テスト観点

- `通常` の `pending_rescue` へ後から viewport 優先要求が来ると `優先` へ昇格する
- `優先` の既存要求へ後から `通常` が来ても降格しない
- `processing_rescue` 中の既存要求は preempt されない
- `PriorityUntilUtc` 切れの一時優先は lease順で通常扱いになる
- `選択救済` が `一括救済` の backlog より先に lease される
- 上側タブの visible range 外 placeholder は自動優先対象にならない
- `サムネ失敗` タブで viewport 行だけを拾える
- duplicate 時に新規 append が増殖せず、既存 main record が昇格する

## 13. Phase 分割

### Phase A: FailureDb 優先土台

- `ThumbnailFailureRecord` と schema へ `Priority` / `PriorityUntilUtc` 追加
- 有効Priority判定 helper を追加
- `GetPendingRescueAndLease(...)` の順序を priority aware にする

### Phase B: duplicate 昇格

- `HasOpenRescueRequest(...)` 依存を減らし、append or promote の入口へ置き換える
- `pending_rescue` の昇格ルールを実装
- `ExtraJson` へ source を残す

### Phase C: 画面表示分の優先付与

- 詳細 placeholder を固定優先へ変更
- 上側タブ visible range の placeholder を一時優先で投入
- `サムネ失敗` タブ viewport 行 helper を追加
- `選択救済` / `一括救済` の priority 分岐を実装

### Phase D: 回帰確認

- 通常動画の一覧操作テンポが落ちていないか確認
- 目の前の `ERROR` が backlog より先に片付くか確認
- duplicate 増殖がないか確認
- 期限切れ一時優先が残留しないか確認

## 14. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| DISPERR-001 | 完了 | FailureDb 優先列追加 | `ThumbnailFailureRecord.cs`, `ThumbnailFailureDbSchema.cs` | `Priority` と `PriorityUntilUtc` を保持できる |
| DISPERR-002 | 完了 | rescue lease順を priority 対応 | `ThumbnailFailureDbService.cs` | 有効 `優先` が `通常` より先に lease される |
| DISPERR-003 | 完了 | duplicate 時の昇格API追加 | `ThumbnailFailureDbService.cs`, `ThumbnailRescueLane.cs` | append 済み `pending_rescue` を昇格できる |
| DISPERR-004 | 完了 | 詳細 placeholder の固定優先化 | `MainWindow.BottomTabs.Common.cs`, `ThumbnailRescueLane.cs` | 今見ている詳細 `ERROR` が固定優先で入る |
| DISPERR-005 | 完了 | 上側タブ visible range の一時優先化 | `MainWindow.BottomTabs.Common.cs`, `MainWindow.UpperTabs.Viewport.cs` | 画面内 placeholder だけが一時優先で入る |
| DISPERR-006 | 完了 | Error タブ viewport 行取得 helper | `MainWindow.ThumbnailFailedTab.cs` | DataGrid の可視行だけを取得できる |
| DISPERR-007 | 完了 | Error タブ選択/一括救済の priority 分岐 | `MainWindow.ThumbnailFailedTab.cs` | 選択行と可視行が先に処理される |
| DISPERR-008 | 完了 | 契約文書と関連計画書の整合確認 | `Implementation Plan_Contracts候補_QueueObj切り出し_2026-03-17.md`, 本書 | `QueueObj.Priority` 利用説明に齟齬がない |
| DISPERR-009 | 完了 | FailureDb priority テスト追加 | `Tests/IndigoMovieManager_fork.Tests/*` | 昇格・lease順・期限切れが固定される |
| DISPERR-010 | 未着手 | 実動画で体感確認 | 確認メモ | 画面内 `ERROR` が backlog より先に解消される |

## 15. リスクと対策

- リスク: viewport を見ただけで `優先` が溜まり続ける
  - 対策: `PriorityUntilUtc` を入れて一時優先を自然失効させる
- リスク: duplicate を skip したままで優先が効かない
  - 対策: append ではなく promote を初版対象に含める
- リスク: `一括救済` 全件を優先化して rescue backlog が暴れる
  - 対策: viewport 行だけ一時優先、残りは通常で固定する
- リスク: DataGrid viewport 取得が重い
  - 対策: タブが visible の時だけ、表示変化契機で差分取得する
- リスク: `QueueObj` 契約文書が再び古い前提へ戻る
  - 対策: 本計画では新規プロパティを増やさず、既存 `Priority` 再利用の説明だけ同期確認する

## 16. 採否基準

- 目の前に見えている `ERROR` が backlog より先に解消されやすくなる
- 通常キューの初動テンポを悪化させない
- RescueWorker の複雑さが preempt まで広がらない
- duplicate 増殖や救済二重実行を起こさない

## 17. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\BottomTabs\Common\MainWindow.BottomTabs.Common.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\BottomTabs\ThumbnailError\ThumbnailErrorTabView.xaml`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\BottomTabs\ThumbnailError\ThumbnailErrorTabView.xaml.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Watcher\MainWindow.ThumbnailFailedTab.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\UpperTabs\Common\MainWindow.UpperTabs.Viewport.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureRecord.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbSchema.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Contracts\QueueObj.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Contracts\Implementation Plan_Contracts候補_QueueObj切り出し_2026-03-17.md`
