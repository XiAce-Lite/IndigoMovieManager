# Implementation Plan（Phase 4: Rust外出し準備 QueueDBハッシュ保持 マイグレーションなし 2026-03-04）

## 1. 目的
- サムネイル生成エンジンの Rust 外出しに備え、QueueDB だけで生成入力を確定できる状態にする。
- `動画名.#hash.jpg` の命名を安定化し、`hash` 欠落時の別名生成を減らす。

## 2. 前提（今回の制約）
- QueueDB の既存データ移行（マイグレーション）は実施しない。
- QueueDB は運用上「再生成可能な作業DB」として扱う。
- 既存 MainDB（`.wb`）のスキーマは変更しない。
- 拡張子方針は「MainDB=`.wb` 維持、QueueDBのみ `.imm` 化」とする。

## 3. 方針（結論）
- QueueDB に `Hash` 列を追加する。
- 既存 QueueDB が旧スキーマの場合は「互換変換」ではなく「再作成」で対応する。
- 重複キーは現行維持（`MainDbPathHash + MoviePathKey + TabIndex`）。`Hash` はキーに含めない。
- MainDB 拡張子は変更しない（`*.wb` 維持）。
- QueueDB ファイル名は `.queue.db` から `.queue.imm` へ変更する。

## 4. スコープ
- IN
  - `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbSchema.cs`
  - `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
  - `src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/QueueRequest.cs`
  - `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - `Thumbnail/QueueObj.cs`
  - `Thumbnail/MainWindow.ThumbnailQueue.cs`
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `Thumbnail/ThumbnailCreationService.cs`
  - Queue関連テスト
- OUT
  - MainDB スキーマ変更
  - QueueDB のデータ移行スクリプト
  - Rust 実装本体

## 5. 実装タスクリスト（計画兼チェックリスト）
- [ ] P4-001: QueueDB スキーマへ `Hash TEXT NOT NULL DEFAULT ''` を追加
- [ ] P4-000: QueueDB 拡張子を `.queue.imm` へ変更（PathResolver / 参照テスト更新）
- [ ] P4-002: Queue DTO へ hash を追加
  - `QueueDbUpsertItem`
  - `QueueDbLeaseItem`
  - `QueueRequest`
- [ ] P4-003: Upsert/Lease SQL を hash 対応へ更新
  - INSERT/UPDATE で hash 保存
  - SELECT で hash 取得
- [ ] P4-004: Producer 側で QueueRequest に hash を設定
  - 監視経路
  - UI手動経路
  - 再作成経路
- [ ] P4-005: Consumer 側で leased hash を `QueueObj.Hash` へ復元
- [ ] P4-006: `ThumbnailCreationService` で `queueObj.Hash` を優先利用（再ハッシュ抑制）
- [ ] P4-007: 旧スキーマ検出時の再作成ルールを実装
  - 旧QueueDBを削除または退避（`*.bak`）
  - 新スキーマで再初期化
- [ ] P4-008: ログ追加
  - 旧スキーマ検出
  - QueueDB再作成
  - hash 欠落フォールバック発生
- [ ] P4-009: テスト更新
  - QueueDB スキーマ検証
  - Upsert/Lease の hash 往復
  - QueueProcessor 復元値検証
- [ ] P4-010: 手動回帰
  - 通常監視追加
  - 既存DB動画の再作成
  - アプリ再起動後の継続処理

## 6. マイグレーションなし運用ルール
- 初回起動時に QueueDB の列構成を検査する。
- `Hash` 列が無い場合は旧DBをそのまま使わず、QueueDBを再作成する。
- 再作成時は Pending ジョブを失うため、必要ジョブは監視再走査または再投入で回復する。

## 7. 完了条件（DoD）
- QueueDB に hash が永続化され、Lease 復元後も `QueueObj.Hash` が保持される。
- `CreateThumbAsync` 開始時点で hash 欠落率が実運用で十分低い。
- 旧スキーマQueueDBでも、マイグレーションなしで起動継続できる（再作成で吸収）。
- ビルドとテストが通る。

## 8. 検証コマンド
- Queue単体:
  - `dotnet build src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj -c Debug`
- 本体:
  - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- テスト:
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build`

## 9. リスクと対策
- リスク: QueueDB再作成で未処理ジョブが消える  
  対策: 監視再走査・欠損サムネ再投入手順を運用手順へ明記。
- リスク: hash が古いまま処理される  
  対策: 必要に応じて `movie_size_bytes` と `last_write` を比較し不一致時のみ再ハッシュ。
- リスク: Rust外出し時の契約不一致  
  対策: QueueDB列仕様を「外部契約」として固定し、仕様書へ反映。
