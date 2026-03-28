# レビュー: Phase 2 後半 retry 縮退 + rescue lane OFF + DLL セッションコピー起動

レビュー日: 2026-03-14

対象変更ファイル:
- src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs（QueueDb retry 5→2）
- Thumbnail/ThumbnailCreationService.cs（autogen retry 4→1）
- Thumbnail/MainWindow.ThumbnailCreation.cs（auto promotion OFF, 外部 worker 接続）
- Thumbnail/MainWindow.ThumbnailRescueLane.cs（明示救済のみ残存）
- Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs（外部 worker 起動 partial）
- Thumbnail/ThumbnailRescueWorkerLauncher.cs（DLL セッションコピー + プロセス管理）
- src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs（HasPendingRescueWork 追加）
- src/IndigoMovieManager.Thumbnail.Engine/AppLocalDataPaths.cs（RescueWorkerSessions パス追加）
- Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs
- Tests/IndigoMovieManager_fork.Tests/ThumbnailFailureDbTests.cs
- Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs
- Thumbnail/Docs/Implementation Plan_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md

---

## Part 1: retry 縮退

### 良い点

**1. 定数変更のみで達成**
`DefaultMaxAttemptCount = 2`（QueueDb）と `DefaultAutogenRetryCount = 1`（autogen）の 2 箇所。
既存の環境変数オーバーライド（`IMM_THUMB_AUTOGEN_RETRY`）も残っており、ロールバックが環境変数 1 つで可能。

**2. テストが縮退後の期待値に追従**
`AutogenExecutionFlowTests` で `AutogenRetryEnvName = "on"` を明示設定して retry=1 以上のシナリオをテストできている。
既定値テストではなく環境変数上書きテストなので、定数変更によるテスト壊れを回避している。

### 指摘

**RET-1 (低): 縮退前の旧テスト期待値の残存確認**
`autogen.CreateCallCount == 2` の assertion は retry=1（初回 + retry 1 回 = 計 2 回呼び出し）に対応している。
これは環境変数 `AutogenRetryEnvName = "on"` で明示有効化した上での結果なので、
既定値 `DefaultAutogenRetryCount = 1` であっても `on` に上書きされた場合のリトライ回数が何回になるか
`ResolveAutogenRetryCount()` の実装を確認した限り、`on` → `DefaultAutogenRetryCount` を返す分岐のため整合している。問題なし。

---

## Part 2: in-proc rescue lane 既定 OFF

### 良い点

**1. `EnableInProcThumbnailRescueAutoPromotion = false` の一点制御**
`ShouldPromoteThumbnailFailureToRescueLane` の冒頭で guard return しており、
timeout handoff と failure handoff の両方を一括で無効化できている。
コードの構造変更なし、フラグ 1 つだけなのでロールバックも `true` に戻すだけ。

**2. 明示救済（UI 操作によるエンキュー）は残存**
`EnsureThumbnailRescueTaskRunning` と `TryEnqueueThumbnailRescueJob` は消されておらず、
ユーザーが意図的に rescue を実行する経路は生きている。
計画書 §16.2 の「in-proc rescue lane は rollback 用にコードを残すが、既定では自動起動を止める」通り。

**3. テストが既定 OFF を明示検証**
`ShouldPromoteThumbnailFailureToRescueLane_Phase2では既定OFFを返す` で、
通常 QueueObj / rescue QueueObj / manual の全パターンが `Is.False` を返すことを確認。
Phase 3 で完全削除する際のテスト消去漏れ防止にもなる。

### 指摘

**LANE-1 (中): timeout handoff 時の挙動変化に注意**
auto promotion が OFF になったことで、通常レーン timeout 時に `TryPromoteThumbnailJobToRescueLane` が `false` を返すようになり、
直後の `throw new TimeoutException(...)` が常に実行される。これ自体は計画通りだが、
timeout → `ThumbnailQueueStatus.Failed` → FailureDb append の流れで、
エラーメッセージに `"thumbnail normal lane timeout"` が入る。
`ResolveFailureKind` は `TimeoutException` → `HangSuspected` を返すので分類は正しいが、
QueueDb 側では `AttemptCount + 1 >= 2` で即 `Failed` になるため、
timeout 1 回で即座に FailureDb 送りになる。これが意図通りなら問題ないが、
**通常動画で一時的な高負荷 timeout が 1 回で即 FailureDb に落ちる** 点は認識しておくべき。

→ Phase 3 以降で timeout threshold を調整する可能性があるなら Note として残す。

---

## Part 3: DLL セッションコピー起動

### 良い点

**1. 起動トリガーが適切**
`onQueueDrainedAsync` コールバック → `TryStartExternalThumbnailRescueWorkerAsync` → `TryStartIfNeeded`。
通常キューが drain した（`leasedItems.Count < 1`）時だけ呼ばれるため、通常作業中に worker が起動して
リソースを食うことがない。`TryGetCurrentQueueActiveCount > 0` の二重チェックも堅い。

**2. debounce 3 秒**
`LaunchDebounce = TimeSpan.FromSeconds(3)` で、drain のたびに worker を起動し続けることを防いでいる。
drain は poll 間隔（通常 3 秒）ごとに呼ばれるため、debounce なしだと worker が短時間に多数起動する。

**3. `HasPendingRescueWork` の軽量チェック**
`SELECT 1 ... LIMIT 1` で実行計画が軽い。`pending_rescue` と lease 期限切れ `processing_rescue` の
両方を拾っており、worker 異常終了後の再起動にも対応。

**4. worker ソース探索の柔軟性**
環境変数 `IMM_THUMB_RESCUE_WORKER_EXE_PATH` → `rescue-worker/` サブフォルダ → 同階層 →
開発ビルドの `bin/x64/Debug/net8.0-windows/` と `Release/` の 5 段フォールバック。
開発時と配布時の両方で動作する。

**5. generation ディレクトリの設計**
`worker_v{version}_{hash}` で exe の version + size + timestamp を含むハッシュを使っており、
ビルドが変わるたびに新しい generation が作られる。同一 version で DLL だけ変わっても
`LastWriteTimeUtc` / `Length` でハッシュが変わるため検出できる。

**6. 掃除方針が計画書 §18 と一致**
`CleanupOldSessions` が generation を `CreationTimeUtc` 降順でソートし、上位 3 世代を残して古いものを削除。
session 単位では `SessionRetention = 7 日` を超えたものを削除。
計画書の「7日超」「最新版3世代より古い」の両条件を満たしている。

**7. Exited イベントでのセッション掃除**
`process.Exited += (_, _) => HandleWorkerExited(...)` で worker 終了時にセッションフォルダを
best effort で削除。長時間動いた worker のセッションが次の cleanup まで残り続けることを防ぐ。

### 指摘

**DLL-1 (高): `CopyDirectoryRecursive` での symlink / junction の考慮がない**

```csharp
foreach (string filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
{
    File.Copy(filePath, destinationPath, overwrite: true);
}
```

救済exe の出力フォルダに symlink や junction が含まれる場合（ffmpeg DLL を symlink で共有するケースなど）、
`Directory.GetFiles` が symlink 先のファイルを返すため、コピー先に実体が作られてディスクを食う。
また、循環 symlink があると無限ループになる可能性がある。

→ 現時点では救済exe の出力に symlink が含まれるケースは低いが、
`FileAttributes.ReparsePoint` チェックを入れるか、コピー対象を拡張子ホワイトリストで絞ると安全。
Phase 2 scope では問題ないが、配布形態が変わった場合の Note として残す。

**DLL-2 (中): `TryStartIfNeeded` で毎回 `new ThumbnailFailureDbService` している**

```csharp
ThumbnailFailureDbService failureDbService = new(mainDbFullPath);
if (!failureDbService.HasPendingRescueWork(DateTime.UtcNow))
```

Phase 1 レビュー指摘 B と同じパターン。drain のたびに `EnsureInitialized()` → DDL チェックが走る。
`ThumbnailFailureDbService` は `isInitialized` フラグでスキップするが、毎回 new のためフラグが効かない。
`HasPendingRescueWork` は `SELECT 1 LIMIT 1` なので実害は小さいが、
`ThumbnailRescueWorkerLauncher` にフィールドとしてキャッシュする方が自然。

ただし `mainDbFullPath` が DB 切替で変わる可能性があるため、
前回と同じ mainDbFullPath ならキャッシュを使い、変わったら作り直す形が良い。

**DLL-3 (中): `HandleWorkerExited` でセッション削除して次回起動時にコピーが毎回走る**

worker が正常終了すると `TryDeleteDirectoryQuietly(sessionDirectory)` でセッションフォルダが消える。
次回 `TryStartIfNeeded` 時に再度 `CopyDirectoryRecursive` が走る。
exe + DLL 一式のコピーは数 MB〜数十 MB になる可能性があり、drain のたびにコピーが起きると I/O 負荷になる。

対応案:
1. generation ディレクトリ直下に「最新セッション」を 1 つ残し、次回はそれを再利用する
2. session を消さず、起動時に「同一 generation 内の既存 session をまず探す」

→ 現状は debounce 3 秒 + `HasPendingRescueWork` チェックで頻度が制限されているため実害は低い。
pending_rescue が大量にある場合（worker 1 回起動で 1 本しか処理しないため、数十件あると数十回コピーされる）
にだけ気になる。Phase 3 以降で検討。

**DLL-4 (低): `Process.Exited` イベントの race**

```csharp
process.Exited += (_, _) => HandleWorkerExited(process, sessionDirectory, log);
if (!process.Start())
```

`EnableRaisingEvents = true` のため、`Start()` 後に即座にプロセスが終了すると
`Exited` イベントが `HandleWorkerExited` を呼び、`currentProcess = null` にした直後に
`TryStartIfNeeded` の続きで `currentProcess = process` に上書きされる可能性がある。
ただし `Start()` → `currentProcess = process` の間で Exited が発火するのは極めてレアで、
発火しても次回 `IsCurrentProcessRunning` で `HasExited` を見て回復できるため実害は無視できる。

---

## Part 4: テスト

### 良い点

**1. `HasPendingRescueWork` テストが lease 後の状態変化を追跡**
append 直後 → `Is.True`、lease 後 → `Is.False`（lease 有効中）、lease 期限切れ後 → `Is.True`。
3 段階の時系列を 1 テストで検証しており、`HasPendingRescueWork` の SELECT 条件の正しさを確認。

**2. rescue lane OFF テスト**
`ShouldPromoteThumbnailFailureToRescueLane_Phase2では既定OFFを返す` で
Phase 2 の振る舞い変更をピンポイントで検証。Phase が変わったらこのテストが壊れるため検出できる。

### 指摘

**T-1 (中): 外部 worker 起動のテストが存在しない**
`ThumbnailRescueWorkerLauncher` はファイルコピー + プロセス起動を含むため
ユニットテストが難しいのは理解できるが、以下が未検証:
- `BuildGenerationDirectory` のハッシュ安定性
- `CleanupOldSessions` の掃除ロジック
- `TryResolveWorkerSourceDirectory` のフォールバック順

これらは static メソッドなので、テスト可能な形で切り出せる。
Phase 3 で worker 起動が本番経路になる前にテストを増やすのが望ましい。

---

## 計画書 vs 実装の整合確認

| 計画書 | 実装 | 一致 |
|---|---|---|
| §16.2 `QueueDb = 2` | `DefaultMaxAttemptCount = 2` | OK |
| §16.2 `autogen retry = 1` | `DefaultAutogenRetryCount = 1` | OK |
| §16.2 rescue lane 既定 OFF | `EnableInProcThumbnailRescueAutoPromotion = false` | OK |
| §16.2 コード残存 rollback 用 | `EnsureThumbnailRescueTaskRunning` / `TryEnqueueThumbnailRescueJob` 残存 | OK |
| §18 セッション専用フォルダコピー | `CopyDirectoryRecursive` → generation + session | OK |
| §18 version + hash | `BuildGenerationDirectory` | OK |
| §18 掃除: 7 日超 + 3 世代 | `CleanupOldSessions` | OK |
| §20 RES-006 / RES-007 完了 | DLL コピー + 掃除実装済み | OK |
| §20 RET-001 / RET-002 / LANE-001 完了 | retry 縮退 + rescue lane OFF | OK |

---

## 総合評価

Phase 2 後半として **マージ可能な状態**。

retry 縮退は定数 2 箇所のみで影響範囲が最小。rescue lane OFF は `static readonly bool` の 1 点制御で
ロールバックが容易。DLL セッションコピーは generation + session の 2 層構造で version 混在を防ぎ、
掃除ルールも計画書通り。

これで Phase 2 の全タスク（RES-001〜007, RET-001〜002, LANE-001）が完了。

### Phase 3（SYNC-001）に進む前に対応推奨

1. 指摘 DLL-2: `ThumbnailFailureDbService` のキャッシュ化（軽微）
2. 指摘 LANE-1: timeout 1 回で即 FailureDb 送りの挙動が意図通りかの確認

### Phase 3 着手時に留意

- 指摘 DLL-3: pending_rescue 大量時のセッションコピー頻度
- 指摘 DLL-1: symlink 対策（配布形態変更時）
- 指摘 T-1: worker launcher の static メソッドテスト追加
