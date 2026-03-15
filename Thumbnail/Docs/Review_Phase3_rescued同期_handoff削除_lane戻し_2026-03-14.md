# レビュー: Phase 3 rescued 同期 + handoff 削除 + Recovery 分類停止

レビュー日: 2026-03-14

対象変更ファイル (Phase 3 前半 + 残件):
- Thumbnail/MainWindow.ThumbnailFailureSync.cs（NEW: rescued → reflected 同期）
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs（GetRescuedRecordsForSync / MarkRescuedAsReflected / ResetRescuedToPendingRescue 追加）
- Thumbnail/MainWindow.ThumbnailCreation.cs（handoff 削除、TryApplyThumbnailPathToMovieRecord 共通化、onQueueDrainedAsync 経由変更）
- MainWindow.xaml.cs（startup sync 呼び出し追加）
- src/IndigoMovieManager.Thumbnail.Queue/ThumbnailLaneClassifier.cs（Recovery 分類停止、サイズベースへ戻す）
- Tests/IndigoMovieManager_fork.Tests/ThumbnailFailureDbTests.cs（rescued sync / requeue テスト追加）
- Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs（CanReflect / TryApply / ResolveLane テスト追加）
- Thumbnail/Docs/Implementation Plan_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md（SYNC-001〒002 / LANE-002〒003 完了反映）

補記: 外部 review finding「slow 失敗が救済経路から脱落する」は、FailureDb の lane 条件を `normal / slow` へ拡張し、`slow -> pending_rescue -> processing_rescue -> rescued -> reflected` の往復テストも追加済みのため解消済み。

---

## Part 1: rescued 同期（MainWindow.ThumbnailFailureSync.cs）

### 良い点

**1. 責務の独立**
`MainWindow.ThumbnailFailureSync.cs` を専用 partial にして同期ロジックを隔離している。
queue drain 経路の `OnThumbnailQueueDrainedAsync` をここに置いたことで、
「drained → rescued sync → external worker 起動」の一連の流れが 1 ファイルで追える。

**2. FailureDbService のキャッシュ化**
`ResolveCurrentThumbnailFailureDbService()` で `mainDbFullPath` 単位にインスタンスを使い回す。
Phase 2 後半レビュー指摘 DLL-2 の対応が同期側では最初から織り込まれている。
`lock` + パス比較 + 差し替えのパターンは堅い。

**3. 同期の再実行防止**
`Interlocked.CompareExchange(ref thumbnailFailureSyncRunning, 1, 0)` で
drain が短間隔で連続しても二重実行を防いでいる。`finally` で必ず解除するため
例外時に永久ロックにならない。

**4. 出力欠損時の pending_rescue 戻し**
`CanReflectRescuedThumbnailRecord` で `File.Exists` を確認し、出力 jpg が消えていれば
`ResetRescuedToPendingRescue` で再救済対象に戻す。計画書 §13.2 の例外遷移通り。
ExtraJson に `"requeue_output_missing"` を書いており、後から理由を追跡できる。

**5. TryApplyThumbnailPathToMovieRecord の共通化**
通常生成時の `CreateThumbAsync` 内と rescued sync の両方で同じ `switch (tabIndex)` を使う。
tabIndex → プロパティ名のマッピングが 1 箇所に収まり、今後 tab 追加時の修正漏れを防ぐ。

**6. MovieId 優先 + MoviePath フォールバック**
`IsSameMovieForFailureRecord` で `resolvedMovieId > 0` なら MovieId で突き合わせ、
なければ MoviePath の大文字小文字無視比較。通常生成側の `IsSameMovieForQueue` と同じ戦略。

**7. reflected_no_ui_match の追跡**
UI 上に対象 movie がない（DB を閉じた、フィルタで非表示など）場合でも
`reflected_no_ui_match` として status は `reflected` に進める。
再起動時に再度拾いなおそうとしないため、無限ループにならない。

### 指摘

**SYNC-1 (中): batch 16 固定で大量 rescued 時の完了に時間がかかる**

`ThumbnailFailureSyncBatchSize = 16` で `GetRescuedRecordsForSync(16)` を 1 回だけ呼ぶ。
rescued が 100 件溜まっている場合、drain のたびに 16 件ずつ処理し、残りは次の drain まで待つ。
drain 間隔は poll 約 3 秒なので、100 件だと最短 18 秒かかる。

startup 時も 1 回しか回さないため、起動時に 100 件あっても 16 件しか消化しない。

→ pending がある間 loop する設計にするか、startup 時だけ上限を上げるかを検討。
ただし 16 件ごとに `RefreshThumbnailErrorRecords()` + `Refresh()` を呼ぶため、
UI スレッド負荷とのトレードオフがある。現状は安全側の選択で妥当。

**SYNC-2 (低): TryResolveMovieIdentityFromDb の戻り値を未チェック**

```csharp
_ = TryResolveMovieIdentityFromDb(record.MoviePath, out resolvedMovieId, out _);
```

戻り値を `_` で捨てている。`resolvedMovieId` が 0 のままでも
`IsSameMovieForFailureRecord` で MoviePath フォールバックが効くので実害はないが、
false 時にデバッグログを出しておくと MoviePath 不一致のトラブルシュートが楽になる。

**SYNC-3 (低): CanReflectRescuedThumbnailRecord の TabIndex 制限が分散**

`record.TabIndex is 0 or 1 or 2 or 3 or 4 or 99` と `TryApplyThumbnailPathToMovieRecord` の
`switch` 分岐が同じ値セットだが、それぞれ独立して定義されている。
片方に tab を追加してもう片方を忘れる可能性がある。
→ 定数セットまたは `TryApplyThumbnailPathToMovieRecord` の戻り値だけで判定する案。
ただし `File.Exists` チェックの前に弾きたい意図があるなら現状で OK。

---

## Part 2: FailureDbService 新メソッド

### 良い点

**1. GetRescuedRecordsForSync の SELECT が status=rescued に限定**
`WHERE Status = 'rescued'` のみ。`reflected` や `gave_up` を拾わない。
ORDER BY は `UpdatedAtUtc ASC, FailureId ASC` で古い rescued から先に処理する。

**2. MarkRescuedAsReflected の WHERE に Status='rescued' ガード**
`reflected` に進めるのは `rescued` からだけ。
`pending_rescue` や `processing_rescue` から直接 `reflected` にはできない。
Phase 2 前半レビュー L-1 の教訓が反映されている。

**3. ResetRescuedToPendingRescue も Status='rescued' ガード付き**
同様に `rescued` からのみ `pending_rescue` に戻せる。
異常状態からの誤巻き戻しを防いでいる。

### 指摘

**DB-1 (低): MarkRescuedAsReflected と ResetRescuedToPendingRescue にトランザクションがない**

各メソッドが単独 UPDATE なのでトランザクション不要と言える一方、
同じ FailureId に対して同時に `MarkRescuedAsReflected` と `ResetRescuedToPendingRescue` が
呼ばれた場合、SQLite の行ロックではなくデータベースロックで直列化される。
WHERE の Status 条件で片方が `updated=0` を返すため実害はないが、
理屈上は `BEGIN IMMEDIATE` で囲む方が意図が明確になる。
現状の呼び出し元が `thumbnailFailureSyncRunning` で排他されているため優先度は低い。

---

## Part 3: handoff 削除と lane 戻し

### 良い点

**1. CreateThumbAsync の大幅スリム化**
`TimeoutException` catch 内の `TryPromoteThumbnailJobToRescueLane` 呼び出しと
その下の `ThumbnailCreateFailureException` catch 内の handoff 呼び出しの両方が削除された。
timeout / failure → 例外 → キュー層で `Failed` → `FailureDb append` の一本道になり、
コードの分岐が減って見通しが良くなった。

**2. ThumbnailLaneClassifier の ResolveLane(QueueObj) がサイズ委譲のみ**
`IsRescueRequest` を見ずに `ResolveLane(movieSizeBytes)` に委譲するだけ。
Recovery enum が lane 判定から完全に外れた。
ResolveRank も Normal=0 / Slow=1 / _=0 だけで、Recovery 固有の rank がない。

**3. テストで Recovery 非分類を固定**
`ResolveLane_Phase3ではIsRescueRequestよりサイズ分類を優先する` で
`IsRescueRequest = true` の QueueObj を渡しても Normal / Slow のどちらかにしかならないことを検証。
Phase 4 で `IsRescueRequest` を消す際に回帰テストとして機能する。

### 指摘

**HAND-1 (低): ShouldPromoteThumbnailFailureToRescueLane / TryPromoteThumbnailJobToRescueLane が残存**

handoff の呼び出し元は消えたが、メソッド定義自体は残っている。
`EnableInProcThumbnailRescueAutoPromotion = false` で常時 false を返すため dead code。
Phase 5 の CLEAN-001 で消す予定なので今は OK だが、
Phase 4 で `IsRescueRequest` を消す際に `ShouldPromoteThumbnailFailureToRescueLane` の
引数 `QueueObj.IsRescueRequest` への参照も消す必要がある点は注意。

**HAND-2 (低): ThumbnailExecutionLane.Recovery enum 値が未削除**

`ThumbnailLaneClassifier` が Recovery を返さなくなったが、enum 値自体は残っている。
進捗表示や telemetry で参照されているため Phase 4 LANE-005 まで残す設計と理解している。
`ResolveRank` の `_ => 0` で Recovery が来ても Normal 扱いになるので実害なし。

---

## Part 4: startup sync

### 良い点

**1. ContentRendered の末尾に配置**
`TryStartInitialThumbnailFailureSync()` は `MainWindow_ContentRendered` の最後に呼ばれる。
UI 表示と Everything 連携の起動後なので、rescued 同期が UI 表示を遅延させない。

**2. Task.Run で fire-and-forget**
`OperationCanceledException` は正常系として握り、それ以外は `DebugRuntimeLog` に書く。
MainWindow の起動シーケンスを阻害しない設計。

### 指摘

**STARTUP-1 (低): fire-and-forget の Task が未保持**

```csharp
_ = Task.Run(async () => { ... }, token);
```

Task を `_` で捨てているため、未処理例外が TaskScheduler.UnobservedTaskException に
飛ぶ可能性がある。try-catch で全例外を握っているため実質発火しないが、
catch で握り損ねた場合（例: `StackOverflowException` — catch 不可だが）は
プロセスを落とす原因になり得る。
→ 現実的には問題ない。念のため `_` ではなくフィールドに保持して shutdown 時に await する案はあるが、
同期 1 batch（16 件）で済むため priority は低い。

---

## Part 5: テスト

### 良い点

**1. rescued → reflected → 再取得しないまでの E2E フロー**
`GetRescuedRecordsForSync_未反映rescuedを返しMarkRescuedAsReflectedで閉じる` が
append → lease → rescued → GetRescuedRecords → MarkReflected → reflected確認 の全手順を
1 テストで検証。reflected 後に GetRescuedRecords が 0 件になることも暗黙に確認できる。

**2. ResetRescuedToPendingRescue の往復テスト**
rescued → pending_rescue に戻して、FailureReason / ExtraJson が書き換わる確認。
出力欠損シナリオの核心。

**3. CanReflectRescuedThumbnailRecord の実ファイルテスト**
一時ファイルを作って `File.Exists` が true になるケース、finally で削除。
モックではなく実 I/O で検証しており信頼性が高い。

**4. ResolveLane 回帰テストの reflection 戦略**
`ThumbnailLaneClassifier` が internal なので reflection で `ResolveLane(QueueObj)` を呼ぶ。
`InternalsVisibleTo` がなくてもテスト可能にしており、テストプロジェクトの参照構成に影響を与えない。

**5. lease 競合テスト（TEST-003）の追加**
Phase 2 前半レビュー T-1 で求めた同時取得テストが入っている。
`ManualResetEventSlim` でスタートゲートを揃え、`WhenAll` 後に 1 件のみ取得を確認。

### 指摘

**T-1 (中): reflected 後の GetRescuedRecords が 0 件になるテストが明示的にない**

`GetRescuedRecordsForSync_未反映rescuedを返しMarkRescuedAsReflectedで閉じる` は
MarkReflected 後に「persisted.Status == reflected」を確認しているが、
もう一度 `GetRescuedRecordsForSync` を呼んで 0 件を確認していない。
WHERE 条件に Status='rescued' があるので 0 件になるはずだが、明示検証があると安心。

**T-2 (低): ResolveLane テストの Slow サイズが 2TB**

```csharp
MovieSizeBytes = 2L * 1024 * 1024 * 1024 * 1024,
```

`DefaultSlowLaneMinGb = 3` なので 3GB 以上が Slow。2TB は明らかに Slow だが、
境界値テスト（3GB - 1 byte = Normal, 3GB = Slow）ではない。
Phase 4 で `ThumbnailSlowLaneMinGb` 設定を変える際に境界値テストが欲しくなるかもしれない。

---

## 計画書 vs 実装の整合確認

| 計画書 | 実装 | 一致 |
|---|---|---|
| §13.1 step 7: `rescued → reflected` | `MarkRescuedAsReflected(Status='reflected')` | OK |
| §13.2 rescued 出力欠損: `rescued → pending_rescue` | `ResetRescuedToPendingRescue` | OK |
| §20 SYNC-001 完了 | `TrySyncRescuedThumbnailRecordsAsync` 実装済み | OK |
| §20 SYNC-002 完了 | `TryStartInitialThumbnailFailureSync` ContentRendered 呼び出し | OK |
| §20 LANE-002 完了 | timeout / failure handoff 呼び出し削除 | OK |
| §20 LANE-003 完了 | `ResolveLane(QueueObj)` → サイズ委譲のみ | OK |
| §16.3 Phase 3 | handoff 停止 + lane サイズベース | OK |
| §21 Phase 3 完了条件 | すべて満たした | OK |

---

## 総合評価

Phase 3 として **マージ可能な状態** 🟢

rescued 同期の設計が堅く、出力欠損時の pending_rescue 戻しまで織り込まれている。
handoff 削除で `CreateThumbAsync` の分岐が大幅に減り、本exeの責務が
「試す → 失敗したら FailureDb へ → rescued が来たら UI に戻す」の一本道に整理された。
lane 分類も Normal/Slow の 2 値に戻り、Phase 4 での `IsRescueRequest` 削除への地ならしが完了。

### Phase 4 に進む前に対応推奨

1. 指摘 SYNC-1: startup 時の batch 上限。起動時に大量 rescued が溜まるようになったら要検討。
2. 指摘 T-1: reflected 後に GetRescuedRecords が 0 件になる明示テスト追加。

### Phase 4 着手時に留意

- 指摘 HAND-1: `ShouldPromoteThumbnailFailureToRescueLane` 内の `IsRescueRequest` 参照
- 指摘 HAND-2: `ThumbnailExecutionLane.Recovery` enum 値の削除タイミング
- 指摘 SYNC-3: TabIndex 有効値セットの一元管理
- 指摘 T-2: Slow lane 境界値テストの追加
