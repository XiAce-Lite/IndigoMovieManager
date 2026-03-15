# レビュー: Phase 2 前半 救済exe最小導入 + FailureDb lease 制御 + 計画書更新

レビュー日: 2026-03-14

対象変更ファイル:
- Thumbnail/Docs/Implementation Plan_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md（計画書全面改稿）
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs（lease API 追加）
- src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj（新規）
- src/IndigoMovieManager.Thumbnail.RescueWorker/Program.cs（新規）
- src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs（新規）
- Tests/IndigoMovieManager_fork.Tests/ThumbnailFailureDbTests.cs（5件に拡張）
- IndigoMovieManager_fork.sln（プロジェクト追加）

---

## 計画書レビュー

### 良い点

**前回レビュー指摘が全件追跡可能に反映されている**
- 指摘 A（fork 側 FailureDb 流用方針）→ §4 で「流用するもの/捨てるもの」が明文化
- 指摘 B（UpdatedAtUtc 欠落）→ §10 補足と §0.1 で対応済み明記
- 指摘 C（本exeが埋める列の定義）→ §11 の 3 区分表で解決
- 指摘 D（過渡期 QueueDb retry）→ §16 で Phase 別に値を固定
- 指摘 F（MainDB 更新パス）→ §6 で方針と理由を明確化
- 指摘 G（Recovery レーン扱い）→ §17 で Phase 別の縮退順を固定

**タスクリスト（§20）が実装と一致している**
Phase 1 の完了済みタスクと Phase 2 の進行中 / 未着手が追跡可能。RVW-001〜004（Phase 1 レビュー反映）も含めて記録されている。

**Phase 2 を前半 / 後半に分割した判断が正しい**
§0.3 の「先に完走経路を固定し、その後で本exeを痩せさせる」は Phase 順の原則（受け皿→本exe縮退）を守っている。

### 指摘

**計画書 P-1: §23 参照ファイルに救済exeのパスが含まれていない**
Phase 2 で追加した `src/IndigoMovieManager.Thumbnail.RescueWorker/*` が §23 の参照ファイル一覧にない。次回更新時に追加すべき。

---

## コードレビュー: FailureDb lease API

### 良い点

**1. `GetPendingRescueAndLease` の `BEGIN IMMEDIATE TRANSACTION` が正しい**
SELECT → UPDATE の間に他 worker が割り込んで同じ行を取得する race を防いでいる。FailureDb は WAL モードなので `IMMEDIATE` で十分。SQLite の排他制御として正しい選択。

**2. lease 期限切れの自動再取得**
SELECT 条件で `Status = 'processing_rescue' AND LeaseUntilUtc < @NowUtc` を含めており、異常終了からの回復が別コードなしで成立する。計画書 §13.2 の「lease 期限切れ → pending_rescue」を SQL 一発で実現している。

**3. `AttemptGroupId` を lease 取得時に採番**
Phase 1 レビュー指摘 D の通り、本exe appends 時は空文字、lease 時に GUID を採番。これで同一動画の救済試行束を追跡できる。

**4. `ReadRecord` の共通化**
Phase 1 で各クエリに分散していたカラム読み出しが `ReadRecord(reader)` に集約された。カラム追加時の修正箇所が 1 箇所になり保守性向上。

### 指摘

**L-1 (中): `UpdateFailureStatus` の WHERE に `Status` 制約がない**

```csharp
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND LeaseOwner = @LeaseOwner;
```

`LeaseOwner` 一致だけで更新するため、既に `rescued` / `gave_up` に遷移済みのレコードに対して再度
`UpdateFailureStatus` を呼ぶと状態が巻き戻る可能性がある。
例えば、heartbeat の timing で `gave_up` 更新直後に別の catch でもう一度 `gave_up` を書く場合は
実害はないが、`rescued` → `gave_up` のような逆遷移を防ぐ安全策として
`AND Status IN ('processing_rescue')` を追加する方が堅い。

→ Phase 2 後半で対応推奨。現状の救済exe は 1 プロセス = 1 動画で、呼び出し順が制御されているため実害は低い。

**L-2 (低): `ExtendLease` の戻り値がない**

`ExecuteNonQuery()` の更新件数を捨てている。lease が失われた（他 worker に奪われた、期限切れで
re-lease された）場合に、呼び出し元が気付けない。
現時点では 1 worker = 1 movie で競合が起きないため実害はないが、将来複数 worker 対応時には
更新件数 0 を検出して処理を中断する仕組みが要る。

---

## コードレビュー: 救済exe (RescueWorkerApplication)

### 良い点

**1. 1 回起動 1 本完結の設計が計画通り**
`GetPendingRescueAndLease` → engine 総当たり → `UpdateFailureStatus` の一直線。
ステート管理が単純で、異常終了しても lease 期限切れで自動回復する。

**2. heartbeat が非同期で独立動作**
`RunLeaseHeartbeatAsync` が `PeriodicTimer` で 60 秒ごとに lease を延長し、
`finally` ブロックで `heartbeatCts.Cancel()` → await による確実な停止を行っている。
`OperationCanceledException` の握り潰しも正しい。

**3. engine 切替が環境変数の save/restore で実装されている**
`ThumbnailEnvConfig.ThumbEngine` を試行前に save、finally で restore。
本exe のエンジン選択ロジック(`ThumbnailEngineRouter`)を一切変更せずに
engine 強制が実現できており、コード変更の影響範囲が最小。

**4. 各試行が `AppendRescueAttemptRecord` で全件記録される**
成功 engine だけでなく失敗 engine の試行も `Lane=rescue` で append されるため、
後から「何番目の engine で何 ms かかって何で落ちたか」を比較できる。
計画書 §12「比較可能性を優先する」が実装に反映されている。

**5. index repair の段階的実行**
direct 試行 → repair 判定 → probe → repair → repaired ソースで再試行の流れが明確。
repair 対象拡張子 (`.mp4`, `.mkv`, `.avi`, `.wmv`, `.asf`, `.divx`) と
error keyword でのゲート判定が、不要な repair を避けている。

**6. 一時修復ファイルの掃除**
`finally` ブロックで `TryDeleteFileQuietly(repairedMoviePath)` が呼ばれ、
修復中ファイルの放置を防いでいる。掃除失敗は成功判定に影響させない方針も正しい。

### 指摘

**R-1 (高): `Environment.SetEnvironmentVariable` はプロセスグローバル**

```csharp
Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, engineId);
```

救済exe は現在 1 プロセス 1 動画なので問題ないが、将来的にプロセス内で
複数動画を並列処理する構成に変わった場合、環境変数はスレッド安全ではない。
この制約を明示するコメントまたは `[ThreadStatic]` 的なガードが欲しい。

→ 現状は暗黙の前提「1 プロセス = 1 動画 = 1 engine 試行」で成立。
`RescueWorkerApplication` クラスのコメントに「1 プロセス前提」が書かれているので
最低限は満たしているが、メソッドレベルにも一言あると安全。

**R-2 (高): `ResolveMainDbContext` が MainDB (\*.wb) を直接 SQLite で読んでいる**

```csharp
using SQLiteConnection connection = new($"Data Source={mainDbFullPath}");
connection.Open();
command.CommandText = "SELECT value FROM system WHERE attr = 'thum' LIMIT 1;";
```

計画書 §6 では「救済exeは MainDB を直接触らない」方針だが、ここでは `system` テーブルから
`thum`（サムネイルフォルダパス）を読み取っている。これは「読み取り」なので書き込みの競合は
ないが、計画書の文面と齟齬がある。

対応案:
1. 計画書を「救済exeは MainDB を**書き込まない**（読み取りは許容）」へ修正する
2. または `--thumb-folder` 引数を追加し、呼び出し元（本exe or スクリプト）が渡す

→ 実用上は (1) で十分。書き込みは計画通り本exe側に閉じており、
読み取りのみの SQLite 接続は WAL モードなら安全。計画書の表現を微修正すれば解消する。

**R-3 (中): `DeleteStaleErrorMarker` で `TabInfo` / `ThumbnailPathResolver` を使っている**

`IndigoMovieManager_fork.csproj` への `ProjectReference` があるため本exe側の型を
直接参照できているが、救済exe が本exe のUI層型（`TabInfo`）に依存している。
Phase 4 以降で救済exe を本exe から分離する際に、この依存がネックになる。

→ Phase 2 scope では問題ない。Phase 4 までに `TabInfo.GetDefaultThumbRoot` と
`ThumbnailPathResolver.BuildErrorMarkerPath` を共通ライブラリへ移すか、
救済exe 用の薄いラッパーを挟む計画を立てておくのが望ましい。

**R-4 (中): 成功時に `File.Exists(createResult.SaveThumbFileName)` で確認しているが、タイミングの隙間がある**

```csharp
bool isSuccess =
    createResult != null
    && createResult.IsSuccess
    && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
    && File.Exists(createResult.SaveThumbFileName);
```

`CreateThumbAsync` が成功を返した直後に別プロセスが jpg を消す可能性は極めて低いが、
NAS 上のサムネイルフォルダで write-through が遅い場合に `File.Exists` が false を返す
ケースが理論上ある。現行の本exe 側にも同様のパターンがあるため整合は取れているが、
Phase 3 以降で NAS 対応を入れる場合は注意。

**R-5 (低): `ResolveFailureKind` が本exe側 (`ThumbnailQueueProcessor`) と重複**

救済exe の `ResolveFailureKind` は `failureReasonOverride` パラメータを追加した拡張版。
ロジック本体（文言マッチング）は `ThumbnailQueueProcessor.ResolveFailureKind` と実質同じ。
Phase 4 で整理する際に共通化候補。

---

## コードレビュー: テスト

### 良い点

**1. lease → rescued の一気通貫テスト**
`GetPendingRescueAndLease_通常失敗行をProcessingRescueで取得しAttemptGroupIdを採番する` と
`UpdateFailureStatus_救済成功時はLeaseを解放し出力先を保持する` が、Phase 2 の完了条件
「`pending_rescue → processing_rescue → rescued/gave_up` が回せる」を直接検証している。

**2. 永続化の読み直し検証**
lease や status 更新 後に `service.GetFailureRecords().Single(...)` で DB から再読み込みして
メモリ上のオブジェクトではなく永続化状態を検証している。

### 指摘

**T-1 (中): lease 競合テストが未実装**
計画書 §21 Phase 2 完了条件に「lease 二重取得が起きない」があるが、テストでは
2 つの worker が同時に lease を取りに行くケースが検証されていない。
タスクリスト TEST-003 として挙がっているが、Phase 2 完了条件の一部なので
後半（RET-001 / RET-002 / LANE-001）の前に入れるのが筋。

**T-2 (低): テスト後の FailureDb ファイルパスが `%TEMP%` ではなく `%LOCALAPPDATA%` に作られる**
`ThumbnailFailureDbPathResolver.ResolveFailureDbPath` は常に `AppLocalDataPaths.FailureDbPath`
（= `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\FailureDb\`）配下に作る。
テストの mainDbPath が一意 GUID なので衝突はないが、テストを繰り返すと
LOCALAPPDATA にゴミの `.failure.imm` ファイルが蓄積する。
`finally` の `TryDeleteSqliteFamily` で消してはいるが、テスト途中のクラッシュでは残る。
CI 環境で問題になる場合は、テスト用の PathResolver を差し替え可能にするか、
テスト後に glob で掃除する teardown を入れることを検討。

---

## 計画書 vs 実装の整合確認

| 計画書の定義 | 実装 | 一致 |
|---|---|---|
| lease 初期 5 分 (§14) | `LeaseMinutes = 5` | OK |
| heartbeat 60 秒 (§14) | `LeaseHeartbeatSeconds = 60` | OK |
| engine 順 ffmpeg1pass→ffmediatoolkit→autogen→opencv (§15) | `RescueEngineOrder` 配列 | OK |
| 本exe append 時 `AttemptGroupId=''` (§12) | Phase 1 レビューで修正済み | OK |
| lease 取得時に AttemptGroupId 採番 (§12) | `GetPendingRescueAndLease` 内 | OK |
| 救済試行は `Lane=rescue` で append (§11.2) | `AppendRescueAttemptRecord` | OK |
| 救済exe は MainDB を書き込まない (§6) | `UpdateFailureStatus(rescued)` のみ、MainDB 非接触 | OK（読み取りは指摘 R-2） |
| 救済exe は jpg 保存する (§5.2) | `ThumbnailCreationService.CreateThumbAsync` が保存 | OK |
| 1 worker = 1 movie = 1 lease (§14) | `RunAsync` で 1 件 lease → 処理 → 終了 | OK |

---

## 総合評価

Phase 2 前半として **マージ可能な状態**。

計画書の全面改稿は前回レビュー指摘を正確に反映しており、タスクリストも実装と一致している。
救済exe の最小完走経路（lease → engine 総当たり → repair → rescued/gave_up）が成立しており、
Phase 2 の前半完了条件を満たしている。

### Phase 2 後半（RET-001 / RET-002 / LANE-001）に進む前に対応推奨

1. 指摘 R-2: 計画書 §6 を「読み取りは許容」へ微修正
2. 指摘 P-1: §23 に救済exe パスを追加
3. 指摘 T-1: lease 競合テスト（TEST-003）を RET-001 より先に入れる

### Phase 2 後半着手時に留意

- 指摘 L-1: `UpdateFailureStatus` に `AND Status IN ('processing_rescue')` ガード追加
- 指摘 R-1: 環境変数の「1 プロセス前提」コメント強化
- 指摘 R-3: 救済exe の UI 層型依存を Phase 4 までに解消する計画
- 指摘 R-5: `ResolveFailureKind` の共通化を Phase 4 候補に積む
