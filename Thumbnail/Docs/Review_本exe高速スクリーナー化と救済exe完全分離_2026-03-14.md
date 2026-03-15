# レビュー: Implementation Plan 本exe高速スクリーナー化と救済exe完全分離

レビュー日: 2026-03-14
対象ファイル: Thumbnail/Implementation Plan_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md

---

## 良い点

**1. workthree ブランチ方針との完全整合**
「ユーザー体感テンポ最優先」の方針と、本exeから重い責務を外す方向性が一致している。
§11 の「本exeは失敗の完遂責務を持たない／高速選別責務を持つ」は明確。

**2. フェーズ順が正しい**
§2 の「先に受け皿を作り、その後で本exeを痩せさせる。逆順は採らない」は取りこぼし防止の鉄則。
Phase 1→2→3→4 の依存関係が論理的に破綻していない。

**3. 現行コードとの対応が具体的**
§13 で Phase 3 の対象に `Thumbnail/MainWindow.ThumbnailRescueLane.cs`、
Phase 4 の対象に `Thumbnail/ThumbnailCreationService.cs` や `Thumbnail/Engines/ThumbnailEngineRouter.cs` が明記されており、
現行コードに実在するファイルとの対応が取れている。

**4. 状態遷移がシンプル**
§8 の 5 状態（`pending_rescue` / `processing_rescue` / `rescued` / `gave_up` / `skipped`）は初版として適切な粒度。
例外遷移（lease 期限切れ→再 `pending_rescue`）も実用的。

---

## 指摘事項

### 重要度: 高

**A. §16 参照ファイルの FailureDb が workthree に存在しない**

参照ファイル末尾の 3 ファイルが `IndigoMovieManager_fork`（別リポジトリ）を指している:
- `IndigoMovieManager_fork\src\...\FailureDb\ThumbnailFailureDebugDbService.cs`
- `IndigoMovieManager_fork\src\...\FailureDb\ThumbnailFailureDebugDbSchema.cs`
- `IndigoMovieManager_fork\src\...\FailureDb\ThumbnailFailureRecord.cs`

workthree 側には FailureDb 実装が一切ない。
Phase 1 で「最小実装を workthree へ入れる」とあるが、fork 側の既存実装をそのまま持ち込むのか、新規設計するのかが不明。
fork 側コードが `ThumbnailFailureDebugDb` という名前（デバッグ用位置づけ）なら、
本計画の `FailureDb`（本番運用前提）とはスキーマや責務が異なる可能性がある。

→ **fork 側の何を流用し、何を捨てるかを明記すべき。**

**B. §7 スキーマに `UpdatedAtUtc` が欠落**

§8 の状態遷移（`pending_rescue` → `processing_rescue` → `rescued` / `gave_up`）を追跡するには更新時刻が必須だが、
§7 の最低限カラム一覧に `UpdatedAtUtc` がない。
lease 更新やハートビート延長の記録にも必要。

**C. 本exe→FailureDb append 時の「何を記録するか」が薄い**

§11 では「`FailureDb` append」「`pending_rescue` を記録して終わり」とあるが、
§7 のカラムのうち本exeが埋める列と救済exeが埋める列の区別がない。例えば:
- `Engine` — 本exeが試した engine（現行は `autogen` 固定）を記録する？
- `FailureKind` / `FailureReason` — 本exeが分類するのか、空で救済exeに任せるのか？
- `AttemptGroupId` — 本exe失敗時にどう採番するか？

→ **「本exeが埋める列」「救済exeが埋める列」「両方が埋める列」の 3 区分表を追加すべき。**

### 重要度: 中

**D. 既存 QueueDb リトライとの過渡期が未定義**

現行の QueueDb は `DefaultMaxAttemptCount = 5` で失敗時に `Status=Pending` へ戻して再処理している
（`ThumbnailCreationService.cs` の autogen retry 4 回 + `ThumbnailQueueProcessor.cs` の lease retry 5 回）。
Phase 1 で FailureDb append を追加するとき、QueueDb 側の retry を即座に 1 に下げるのか、
Phase 4 まで並行運用するのかが不明。

→ **Phase 1〜3 の過渡期で「QueueDb retry 回数をいつ削るか」を明記すべき。**

**E. §9 lease 制御のハートビート間隔・期限が未定義**

「長めでよい」「ハートビート延長を持つ」とあるが具体値がない。
現行 QueueDb は lease 30 秒 + ハートビートで運用中。
救済exeは「1 本ずつ最後まで完遂」で数分〜数十分かかる可能性があり、
初期 lease 値とハートビート間隔の目安（例: 初期 5 分、ハートビート 60 秒）があると Phase 2 の実装判断が速くなる。

**F. 救済exe → MainDB 更新の責務分担が曖昧**

§3.2 に「成功時のみ…必要な DB 更新を行う」とあるが、
MainDB（WhiteBrowser 互換 `*.wb`）は本exe が UI と同居で操作しているものである。
救済exeが別プロセスから MainDB を直接更新するのか、
本exeに通知して UI スレッドで更新させるのか、
あるいは FailureDb の `rescued` 状態を本exe起動時にスキャンして反映するのか。
AGENTS.md 「WhiteBrowserのDB(*.wb)は変更しない」ルールとの整合も確認が必要。

→ **MainDB 更新パスを明確にすべき。**

**G. §4「本exeから削る責務一覧」にレーン分類の扱いが不在**

現行の `ThumbnailLaneClassifier` は `Normal` / `Slow` / `Recovery` の 3 レーンを持っている。
Phase 3 で rescue lane を停止したとき、`Recovery` レーンの定義と `IsRescueRequest` フラグの扱い
（残す？削る？`Slow` に統合？）が計画に書かれていない。

### 重要度: 低

**H. §7 `MainDbPathHash` の算出ルールが未定義**

QueueDb 側には `QueueDbPathResolver` が hash 管理を持っているが、
FailureDb 用に同じロジックを流用するのか別にするのかが不明。
初版は共有で問題ないと思われるが、方針を一言添えると良い。

**I. §10 DLL コピーの掃除タイミング**

「終了後の掃除は best effort」は妥当だが、ディスク圧迫時の方針がない。
救済exeが長期運用されると version × hash フォルダが蓄積する。
「起動時に古いフォルダを掃除する」等の一文があると安心。

**J. §12 救済exeの engine 総当たり順序が未定義**

§12 の手順 3〜7 は概念的な順序だが、具体的な engine 試行順
（現行の rescue lane は `ffmpeg1pass → FFMediaToolkit → autogen`）を引き継ぐのか変えるのかが未記載。

---

## 総合評価

方針として **採用価値が高い**。

workthree の最優先目標「通常動画の体感テンポ向上」に直結しており、
本exeの hot path を軽くする方向性は現行コードの構造
（rescue lane が MainWindow partial class に同居、autogen retry + QueueDb retry の二重リトライ）
を見ても合理的。

### 実装に着手する前に明確化すべき最優先事項

1. fork 側 FailureDb の流用方針（指摘 A）
2. 本exeが FailureDb に埋める列の定義（指摘 C）
3. 過渡期の QueueDb retry 回数の扱い（指摘 D）
4. 救済exe → MainDB 更新パス（指摘 F）

これら 4 点が固まれば、Phase 1 の実装に入れる状態。
