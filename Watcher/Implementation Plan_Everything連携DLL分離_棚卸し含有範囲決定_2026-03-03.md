# Implementation Plan（Everything連携DLL分離: 棚卸し・含有範囲・ラッパー方針）

## 1. 目的
- 将来のフルスクラッチ実装を見据え、Everything連携を差し替え可能な境界で分離する。
- 現行機能を棚卸しし、DLLへ含める範囲（IN）と本体へ残す範囲（OUT）を固定する。
- 実装順を「Everything実装 + 汎用呼び出しラッパー先行」に統一し、手戻りを防ぐ。

## 2. 方針決定（2026-03-03）
- 決定:
  - **先に「Everythingプロバイダ + 汎用呼び出しラッパー」を作成する。**
  - フルスクラッチは次段で `ScratchProvider` を追加し、同一契約で差し替える。
- 理由:
  - 先に契約とラッパーを作れば、後続の実装差し替えが局所化できる。
  - 現行動作（OFF/AUTO/ON、フォールバック、reasonコード）を維持したまま移行できる。

## 3. 前提と非目標
- 前提
  - WhiteBrowser互換を維持する。
  - 既存DB（`*.wb`）スキーマは変更しない。
  - 現行reasonコード互換を維持する。
- 非目標
  - この計画段階ではフルスクラッチ本体の実装は行わない。
  - UI文言や通知UXの刷新は対象外。

## 4. 現機能の棚卸し（2026-03-03時点）

### 4.1 設定・依存
- `CommonSettingsWindow.xaml`
  - `EverythingIntegrationMode`（OFF/AUTO/ON）
- `CommonSettingsWindow.xaml.cs`
  - `EverythingIntegrationMode` 保存
  - 旧設定 `EverythingIntegrationEnabled` 同期
- `Properties/Settings.settings`
  - `EverythingIntegrationMode`（int）
  - `EverythingIntegrationEnabled`（bool）
- `IndigoMovieManager_fork.csproj`
  - `EverythingSearchClient` 依存

### 4.2 Everythingアクセス層
- `Watcher/EverythingFolderSyncService.cs`
  - `IsIntegrationConfigured()`
  - `CanUseEverything(out reason)`
  - `TryCollectMoviePaths(...)`
  - `TryCollectThumbnailBodies(...)`
  - `SearchLimit` 打ち切り検知（`everything_result_truncated`）
  - 拡張子分割クエリ、時刻条件、重複除外

### 4.3 MainWindow側オーケストレーション
- `MainWindow.xaml.cs`
  - `RunEverythingWatchPollLoopAsync`
  - `ResolveEverythingWatchPollDelayMs`
  - `ShouldRunEverythingWatchPoll`
- `Watcher/MainWindow.Watcher.cs`
  - `ScanFolderWithStrategyInBackground`
  - `BuildExistingThumbnailBodySet`
  - `IsEverythingEligiblePath`
  - `DescribeEverythingDetail`
  - `LoadEverythingLastSyncUtc` / `SaveEverythingLastSyncUtc`

## 5. DLL含有範囲（IN/OUT）

### 5.1 IN（DLLへ含める）
- 契約（Contracts）
  - `IFileIndexProvider`
  - `FileIndexQueryOptions`
  - `FileIndexMovieResult`
  - `FileIndexThumbnailBodyResult`
  - `IntegrationMode`（OFF/AUTO/ON）
  - reasonコード定義（定数）
- 実装（Providers）
  - `EverythingProvider`（現行相当）
  - パス適格性判定（UNC/DriveType/NTFS）
  - Everything IPC問い合わせ、クエリ生成、結果正規化
- ラッパー（Facade）
  - `IndexProviderFacade`
  - OFF/AUTO/ON判定
  - Provider選択
  - 失敗時フォールバック指示（reason返却）

### 5.2 OUT（アプリ本体へ残す）
- `Properties.Settings` 永続化
- 通知表示（`Notification.Wpf`）
- ログ出力（`DebugRuntimeLog`）
- `CheckFolderAsync` 全体制御（DB登録、UI反映、サムネキュー投入）
- `system` テーブルI/O（last_sync保存/読込）
- ポーリング間隔の動的制御（キュー負荷連動）

### 5.3 境界ルール
- DLLは `System.Windows` と `DataTable` に依存しない。
- DLLはDBアクセスを持たない。
- DLLは通知文言を持たず、reasonコードのみ返す。

## 6. 実装シーケンス（更新）

### Phase 1: 契約固定（最優先）
- [x] reasonコード一覧を固定し互換表を作成する。
- [x] `IFileIndexProvider` の入出力契約を確定する。
- [x] `IndexProviderFacade` の責務（選択/フォールバック/返却値）を確定する。
- [x] Phase 1詳細タスクに沿って実施する（`Watcher/Implementation Plan_Everything連携DLL分離_Phase1詳細_2026-03-03.md`）。

成果物:
- `Watcher/Everything_reason_code契約_2026-03-03.md`
- `Watcher/Everything_DLL_API案_2026-03-03.md`
- `Watcher/Everything_フォールバック条件表_2026-03-03.md`

### Phase 2: Everything + ラッパー実装
- [x] `EverythingProvider` を現行ロジック準拠で実装する。
- [x] `IndexProviderFacade` を実装する。
- [x] `MainWindow` 側はFacade呼び出しへ置換する（挙動不変）。
- [x] Phase 2詳細タスクに沿って実施する（`Watcher/Implementation Plan_Everything連携DLL分離_Phase2詳細_2026-03-03.md`）。

成果物:
- `EverythingProvider` 実装
- `IndexProviderFacade` 実装
- `MainWindow` 呼び出し差し替え

### Phase 3: フルスクラッチ受け皿準備
- [ ] `ScratchProvider` 用のスタブ実装を追加する。
- [ ] 実行時選択フラグ（例: provider種別）を追加する。
- [ ] 同一入力で `EverythingProvider` と同等判定になる比較観点を定義する。

成果物:
- `ScratchProvider` スタブ
- 比較検証仕様

### Phase 4: フルスクラッチ実装（次段）
- [ ] `ScratchProvider` を実装し、Facade経由で切替可能にする。
- [ ] reasonコード契約を維持したまま置換する。
- [ ] 退行試験（候補数、reason、フォールバック一致）を完了する。

## 7. 受け入れ条件
- 先行段階（Phase 2）で、現行と同じ入力に対し候補数とreasonが一致する。
- `MainWindow` は `IFileIndexProvider` / `IndexProviderFacade` だけを参照し、Everything具体型へ直接依存しない。
- `TotalItems > NumItems` 時は必ずフォールバックとなり、`everything_result_truncated` を返す。
- 後続段階（Phase 4）で、`ScratchProvider` への切替時にUI/DBコード変更が不要である。

## 8. リスクと対策
- リスク: 汎用化しすぎて契約が肥大化する
  - 対策: 監視で実際に使う2機能（動画候補・サムネBody）に限定する
- リスク: reasonコード変更で既存通知解釈が壊れる
  - 対策: reasonは互換固定し、新規は後方互換を保って追加する
- リスク: DB時刻永続化責務の混入
  - 対策: last_syncはホスト側責務に固定し、DLLは値のみ扱う

## 9. 次アクション
1. `ScratchProvider` スタブを追加し、`IFileIndexProvider` 契約で差し替え可能な最小実装を作る。
2. provider選択フラグ（例: `Everything` / `Scratch`）を設計し、Facadeへ注入する方式を固定する。
3. `EverythingProvider` と `ScratchProvider` の比較観点（候補数、reason、fallback）をチェックリスト化する。
4. `EverythingFolderSyncService` の最終扱い（削除 or 移行完了まで保持）をPhase3開始時に決定する。

## 10. 実施記録（2026-03-03）
- 完了:
  - `Watcher/Everything_reason_code契約_2026-03-03.md`
  - `Watcher/Everything_DLL_API案_2026-03-03.md`
  - `Watcher/Everything_フォールバック条件表_2026-03-03.md`
  - `Watcher/Everything_Phase2_移植単位一覧_2026-03-03.md`
  - `Watcher/Everything_MainWindow置換ポイント一覧_2026-03-03.md`
  - `Watcher/FileIndexContracts.cs`
  - `Watcher/IFileIndexProvider.cs`
  - `Watcher/EverythingProvider.cs`
  - `Watcher/IndexProviderFacade.cs`
  - `Watcher/MainWindow.Watcher.cs`（Facade経由へ置換）
  - `MainWindow.xaml.cs`（ポーリング判定をFacade経由へ置換）
- 実装コミット:
  - `ad63ee6` `refactor(watcher): phase2 Everything provider facade導入`

## 11. TODO（今朝ログ重複調査の対応）
- [ ] `watch-check enqueue by missing-tab-thumb` のログを `movie+tab` 単位で一定時間サプレッションする（ログ氾濫を抑制）。
- [ ] `TryEnqueueThumbnailJob` 前に、同一 `movie+tab` が QueueDB で `Pending/Processing` なら再投入を抑止する方針を決める。
- [ ] `scan file summary` で `scanned=1 new=1` が連続した場合に、対象動画パスを低頻度で補助ログ出力できるようにする（切り分け用）。
- [ ] `D:\\maimai共有\\IMG_6294_re.mp4` を再現ケースに、タブ別サムネ生成完了後に再検知が止まることを確認する。
- [ ] 受け入れ条件を追加する（同一動画の重複ログ回数、重複キュー投入回数、サムネ生成完了までの時間上限）。

<!-- Codex: 変更通知リンク生成のための最小更新 (2026-03-03) -->
