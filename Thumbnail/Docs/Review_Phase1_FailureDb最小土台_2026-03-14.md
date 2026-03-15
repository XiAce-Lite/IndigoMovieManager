# レビュー: Phase 1 FailureDb 最小土台 + QueueDb 接続

レビュー日: 2026-03-14

対象変更ファイル:
- src/IndigoMovieManager.Thumbnail.Engine/AppLocalDataPaths.cs
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbPathResolver.cs
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbSchema.cs
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureRecord.cs
- src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs
- src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs
- Tests/IndigoMovieManager_fork.Tests/ThumbnailFailureDbTests.cs

---

## 良い点

**1. 変更スコープが正しく絞られている**
計画書 Phase 1 の完了条件（本exe失敗時に FailureDb へ 1 試行 1 行 append できる、`pending_rescue` を記録できる）を過不足なく満たしている。
現行 rescue lane を一切触っていない点も計画通り。

**2. 既存規約の適切な共有**
`ThumbnailFailureDbPathResolver` が `QueueDbPathResolver.GetMainDbPathHash8()` / `CreateMoviePathKey()` をそのまま委譲しており、
ハッシュ算出ルールの二重実装を回避できている。
前回レビュー指摘 H（hash 規約）への回答になっている。

**3. `UpdatedAtUtc` 対応済み**
前回レビュー指摘 B（スキーマに `UpdatedAtUtc` 欠落）が解決されている。
DDL の `DEFAULT (strftime(...))` と C# 側の `DateTime.UtcNow` 初期化も整合。

**4. `TryAppendTerminalFailureRecord` の例外隔離**
FailureDb append 失敗が本体の QueueDb 状態遷移を壊さない設計。
`try-catch` でログだけ出して握るのは Phase 1 の観測土台として正しい判断。

**5. テストが本番接続パスを通している**
`HandleFailedItem_最終失敗時はFailureDbへPendingRescueを追記する` がリフレクションで `HandleFailedItem` を呼び、
QueueDb → FailureDb の一気通貫を検証しているのは良い。

**6. `ResolveFailureKind` の軽量分類が実用的**
`TimeoutException` → `HangSuspected`、`FileNotFoundException` → `FileMissing`、ファイル存在 + 0byte → `ZeroByteFile` など Phase 1 に十分な粒度。
文言判定も `drm` / `moov atom not found` / `no video stream` 等、現行エンジンが実際に投げるメッセージに対応している。

---

## 指摘事項

### 重要度: 高

**A. `ThumbnailFailureDbSchema.EnsureCreated` で PRAGMA 構成が紛らわしい**

```csharp
// ThumbnailFailureDbSchema.cs L49-50
ApplyConnectionPragmas(connection);
QueueDb.QueueDbSchema.ApplyPragmas(connection);
```

`ApplyConnectionPragmas` が `busy_timeout` + `synchronous` を設定した後に `QueueDbSchema.ApplyPragmas` が再び同じ PRAGMA を設定し、さらに `EnsureWalMode` を呼んでいる。動作上の問題は今のところないが:
- FailureDb に対して `QueueDbSchema.ApplyPragmas()` を呼ぶのは名前の意味的に紛らわしい
- `ApplyConnectionPragmas` の設定が内部で即座に上書きされるため冗長

→ **FailureDb 用に `ThumbnailFailureDbSchema` 内に WAL 設定を自前で持つか、共通 PRAGMA ヘルパーを切り出す方が素直。**
Phase 2 で FailureDb 固有の PRAGMA を入れたくなったときに混乱しやすいので Note として残す。

**B. `TryAppendTerminalFailureRecord` で毎回 `new ThumbnailFailureDbService` している**

```csharp
// ThumbnailQueueProcessor.cs L750
ThumbnailFailureDbService failureDbService = new(queueDbService.MainDbFullPath);
```

失敗のたびに `EnsureInitialized()` → `Directory.CreateDirectory` → SQLite 接続 → `EnsureCreated`（テーブル作成 + PRAGMA 発行）を実行する。
2 回目以降は `isInitialized = true` でスキップされるべきところが、**毎回インスタンスを new しているため `isInitialized` が常に false**。

Phase 1 では terminal failure（QueueDb で Failed に落ちた最終失敗）だけなので頻度は低いが、
Phase 2 以降で append 対象を広げた場合にパフォーマンス問題になる。

→ **`ThumbnailFailureDbService` を `ThumbnailQueueProcessor` のフィールドに持たせるか、static な遅延初期化フラグで DDL 発行を一度だけに制限するのが望ましい。**
Phase 1 scope としては low priority。

### 重要度: 中

**C. `LeaseOwner` に本exe側の `ownerInstanceId` を入れている**

```csharp
// ThumbnailQueueProcessor.cs L770
LeaseOwner = ownerInstanceId ?? "",
```

計画書 §9 では `LeaseOwner` は「救済exeが lease 取得した主体」を示す列。
本exe側が append 時に `LeaseOwner` を埋めると、Phase 2 で救済exeが lease 取得する際に上書きが必要になる。
初版として空文字の方が意味的には正しいが、「誰が最初にこのレコードを作ったか」の追跡に使う意図なら列の意味をコメントで補うのが良い。

**D. `AttemptGroupId` が毎回新しい GUID**

```csharp
// ThumbnailQueueProcessor.cs L766
AttemptGroupId = Guid.NewGuid().ToString("N"),
```

計画書 §7 では `AttemptGroupId` は「同じ動画の一連の救済束を追う」ためのもの。
Phase 1 で本exeが append するときは救済束がまだ存在しないので、救済exeが lease 取得時にグループを割り当てる設計の方が自然。
現状だと 1 レコード = 1 グループになり、Phase 2 で「同じ動画の複数試行を紐付ける」ときに使えない。

→ **空文字で入れておき、救済exeが束を開始するときに採番する方が計画と合う。**

**E. `ResolveFailureLaneName` が `IsRescueRequest` を見ていない**

```csharp
// ThumbnailQueueProcessor.cs L798
ThumbnailExecutionLane lane = ThumbnailLaneClassifier.ResolveLane(
    leasedItem?.MovieSizeBytes ?? 0
);
```

サイズだけを渡しているため、Recovery レーンからの失敗も lane が常に `normal` / `slow` になる。
`HandleFailedItem` の呼び出し時点では `QueueDbLeaseItem` に `IsRescueRequest` がないため現状は仕方ないが、
Phase 3 で rescue lane を FailureDb へ接続する際に修正が必要な箇所。

→ **Phase 3 で要対応。コメントを残しておくと良い。**

### 重要度: 低

**F. `GetFailureRecords` に件数制限がない**

現時点ではテスト用だが、本番で数万件溜まった場合にメモリを食う。
Phase 2 で rescue lease 取得メソッドを追加する際に `LIMIT` 付きクエリが入ると思われるが、
`GetFailureRecords` を汎用ダンプとして残すなら上限を入れておくと安全。

**G. テストの `InvokeHandleFailedItem` がリフレクション依存**

`HandleFailedItem` が `private static` なのでリフレクション経由で呼んでいるが、
シグネチャ変更時にコンパイルエラーにならずテストだけ壊れる。
Phase 2 以降で `HandleFailedItem` のパラメータが変わる可能性が高いので、
テスト用の internal アクセスを `InternalsVisibleTo` で開けるか、
テスト側の入口を public な `ProcessSingleJob` 相当にするかを検討してもよい。

---

## 総合評価

Phase 1 の完了条件を満たしており、**マージ可能な状態**。

現行 rescue lane を壊しておらず、FailureDb append が terminal failure 限定で安全に隔離されている。
前回レビューの指摘 B（`UpdatedAtUtc`）と H（hash 規約共有）が解決されているのも確認済み。

### Phase 2 に進む前に対応推奨

1. 指摘 B: `ThumbnailFailureDbService` の毎回 new を避ける（or static 初期化フラグ）
2. 指摘 D: `AttemptGroupId` を空文字にして救済exe側で採番する設計に揃える

### Phase 2 着手時に留意

- 指摘 A の PRAGMA 構成整理（FailureDb 固有の WAL 設定）
- 指摘 C の `LeaseOwner` 列の意味確定
- 指摘 E の lane 判定を `QueueObj` ベースに拡張
- 指摘 F の `GetFailureRecords` に `LIMIT` 追加
