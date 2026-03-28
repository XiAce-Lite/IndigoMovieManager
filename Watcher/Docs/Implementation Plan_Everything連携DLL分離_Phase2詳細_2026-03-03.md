# Implementation Plan（Everything連携DLL分離 Phase 2 詳細）

## 0. Phase2で変更したコードファイルリスト
- 方針:
  - Phase2は「実装フェーズ」だが、既存挙動を変えない置換を最優先にする。
  - 置換は段階実施し、常にフォールバック可能な状態を維持する。
- 新規作成:
  - `Watcher/FileIndexContracts.cs`
    - `IntegrationMode` / `FileIndexQueryOptions` / `FileIndexMovieResult` / `FileIndexThumbnailBodyResult` を定義。
  - `Watcher/IFileIndexProvider.cs`
    - `CheckAvailability` / `CollectMoviePaths` / `CollectThumbnailBodies` の契約を定義。
  - `Watcher/EverythingProvider.cs`
    - `EverythingSearchClient` 呼び出しを集約したProvider本体を実装。
  - `Watcher/IndexProviderFacade.cs`
    - OFF/AUTO/ON判定、Provider呼び出し、fallback判定を実装。
- 既存変更:
  - `Watcher/MainWindow.Watcher.cs`
    - `TryCollectMoviePaths` / `TryCollectThumbnailBodies` 呼び出しをFacade経由へ置換。
  - `MainWindow.xaml.cs`
    - ポーリング可否判定のEverything依存呼び出しをFacade経由へ置換。

## 1. 目的
- `EverythingFolderSyncService` 直結を解消し、`EverythingProvider + IndexProviderFacade` 構成へ移行する。
- Phase1で固定した契約（reason/API/fallback）をコードへ反映する。
- 将来の `ScratchProvider` 追加時にMainWindow側の変更を最小化する。

## 2. スコープ
- 対象
  - Provider/Facade/Contractsの新規実装
  - MainWindow側の呼び出し置換
  - reason互換確認（ログ/通知の意味一致）
- 非対象
  - `ScratchProvider` 実装
  - UI通知文言の刷新
  - DBスキーマ変更

## 3. 依存ドキュメント
- `Watcher/Everything_reason_code契約_2026-03-03.md`
- `Watcher/Everything_DLL_API案_2026-03-03.md`
- `Watcher/Everything_フォールバック条件表_2026-03-03.md`
- `Watcher/Everything_Phase2_移植単位一覧_2026-03-03.md`
- `Watcher/Everything_MainWindow置換ポイント一覧_2026-03-03.md`

## 4. タスクリスト（Done定義つき）

### T1: Contracts 実装
- [x] `FileIndexContracts.cs` を作成し、Phase1確定済みDTO/enumを実装する。
- [x] `IFileIndexProvider.cs` を作成し、APIシグネチャをPhase1契約に一致させる。
- Done定義:
  - DTO名とメンバー名が `Everything_DLL_API案_2026-03-03.md` と完全一致している。

### T2: EverythingProvider 実装（可用性）
- [x] `CheckAvailability` を実装する。
- [x] `setting_disabled` / `everything_not_available` / `availability_error:*` を返却できるようにする。
- Done定義:
  - 既存 `CanUseEverything` と同等条件でreasonが返る。

### T3: EverythingProvider 実装（動画候補）
- [x] `CollectMoviePaths` を実装する。
- [x] `everything_result_truncated:*` / `everything_query_error:*` / `ok:*` を返却できるようにする。
- [x] UTC時刻扱いを現行同等にする（`ChangedSinceUtc`, `MaxObservedChangedUtc`）。
- Done定義:
  - 既存 `TryCollectMoviePaths` と同一入力時に候補件数とreasonの意味が一致する。

### T4: EverythingProvider 実装（サムネBody）
- [x] `CollectThumbnailBodies` を実装する。
- [x] `everything_thumb_query_error:*` / `everything_result_truncated:*` / `ok` を返却できるようにする。
- Done定義:
  - 既存 `TryCollectThumbnailBodies` と同一入力時に集合件数とreasonの意味が一致する。

### T5: IndexProviderFacade 実装
- [x] OFF/AUTO/ON判定をFacadeに集約する。
- [x] `auto_not_available` をFacadeで組み立てる。
- [x] 動画/サムネともにfallback時の戻り値を条件表どおりに返す。
- Done定義:
  - mode判定がProviderへ漏れていない。

### T6: MainWindow置換（Watcher側）
- [x] `MainWindow.Watcher.cs` のEverything呼び出しをFacade経由へ置換する。
- [x] `strategy` / `reason` 受け取り処理を新DTOへ置換する。
- [x] `DescribeEverythingDetail` の解釈ロジックに退行がないことを確認する。
- Done定義:
  - `_everythingFolderSyncService` 直接参照がWatcher側から消える（もしくは完全に未使用化される）。

### T7: MainWindow置換（xaml.cs側）
- [x] `ShouldRunEverythingWatchPoll` の判定をFacade経由へ置換する。
- [x] ポーリング間隔制御ロジックには影響を与えない。
- Done定義:
  - ポーリング判定が従来と同じ前提（DB有効 + 対象パスあり）で動作する。

### T8: 互換レビュー
- [x] reason契約との差分レビューを実施する。
- [x] fallback条件表との差分レビューを実施する。
- [x] 差分が出た場合は「コード修正」か「契約更新」のどちらかへ必ず寄せる。
- Done定義:
  - 契約・実装・挙動の三者で矛盾がない。

### T9: ビルド確認（ルール準拠）
- [x] MSBuildでビルド確認を実施する。
- [x] 失敗時は原因特定後に再試行し、最大3回までとする（今回は1回成功で再試行なし）。
- [x] フォーマット起因が疑わしい場合は `CSharpier` で整形する（今回は実行不要）。
- Done定義:
  - 1回以上ビルドが成功、または失敗原因と再試行履歴が記録されている。

### T10: 完了判定
- [x] Phase2成果物を親計画へ記録する。
- [x] 残課題をPhase3へ引き継ぐ。
- Done定義:
  - Phase2の進捗が親計画に反映され、次アクションが更新されている。

## 5. 受け入れ条件（Phase2 完了条件）
- `MainWindow` が `IFileIndexProvider` / `IndexProviderFacade` 経由で動作する。
- reasonコード互換が維持される（`setting_disabled`, `auto_not_available`, `everything_result_truncated:*`, `everything_thumb_query_error:*`, `ok:*`, `ok`）。
- fallback条件表どおりに `strategy` が返る。
- ビルドルール（MSBuild、最大3回、原因特定先行）を満たす。

## 6. 実施順（推奨）
1. T1
2. T2
3. T3
4. T4
5. T5
6. T6
7. T7
8. T8
9. T9
10. T10

## 7. リスクと対策
- リスク: 置換途中で呼び出し経路が混在し、挙動差分が見えにくくなる
  - 対策: T6/T7完了時点で旧参照の残存を明示チェックする
- リスク: reason生成責務がProvider/Façadeで再び分散する
  - 対策: `auto_not_available` はFacade専任、Providerは返さないルールを固定する
- リスク: ドキュメントと実コードの名前ズレ
  - 対策: T8で型名・メンバー名を契約ドキュメントと突合する

## 8. Open Questions（Phase2開始時点）
- `EverythingFolderSyncService` をPhase2完了時に削除するか、Phase3まで残すか。
- `IsEverythingEligiblePath` をPhase2でFacadeへ寄せるか、後段で実施するか。

## 9. 実施記録（2026-03-03）
- 実装コミット:
  - `ad63ee6` `refactor(watcher): phase2 Everything provider facade導入`
- 主要追加ファイル:
  - `Watcher/FileIndexContracts.cs`
  - `Watcher/IFileIndexProvider.cs`
  - `Watcher/EverythingProvider.cs`
  - `Watcher/IndexProviderFacade.cs`
- 主要変更ファイル:
  - `Watcher/MainWindow.Watcher.cs`
  - `MainWindow.xaml.cs`
- ビルド確認:
  - 実行: `MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m`
  - 結果: 成功（エラー0、警告は `NETSDK1206` 1件）
