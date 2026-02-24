# Implementation Plan（監視イベント分離 + 完全非同期化）

## 1. 目的
- `MainWindow.Watcher.cs` に集中している監視責務を `Watcher` フォルダへ分離する
- ファイル監視イベント経路から同期ブロック（`Thread.Sleep` / 同期DB待ち）を除去する
- UI応答性を維持したまま、監視・DB反映・サムネイルキュー投入を安全に継続する

## 2. 対象範囲
- 対象
  - `MainWindow.Watcher.cs` の `FileChanged` / `FileRenamed` / `RunWatcher` / `CreateWatcher`
  - 監視イベント起点の DB 更新とサムネイルキュー投入
- 非対象（今回）
  - サムネイル生成本体（`ThumbnailCreationService`）のアルゴリズム変更
  - DB スキーマ変更

## 3. 現状課題
1. `FileSystemWatcher` イベント内で同期待機（`Thread.Sleep`）がある
2. 監視イベント内で同期 DB 取得と UI 反映が混在している
3. `async` シグネチャでも同期実装の DB 処理があり、実質ブロックが残る
4. 監視イベントの責務が `MainWindow` に密結合している

## 4. 目標アーキテクチャ（Watcher フォルダ分離）
- 追加フォルダ: `Watcher/`
- 追加クラス（案）
  - `WatcherRegistrationService.cs`
  - `WatcherEvent.cs`
  - `WatcherEventChannel.cs`
  - `WatcherEventProcessor.cs`
  - `WatcherFileReadyChecker.cs`
  - `WatcherDbGateway.cs`
  - `WatcherUiBridge.cs`
- 役割分離
  - 監視登録: `WatcherRegistrationService`（`FileSystemWatcher` の生成/破棄）
  - イベント受信: `WatcherEventChannel`（`Channel<WatcherEvent>`）
  - 業務処理: `WatcherEventProcessor`（非同期で逐次/並列制御）
  - UI更新: `WatcherUiBridge`（`Dispatcher.InvokeAsync` に限定）

## 5. 完全非同期化の設計方針
1. `FileSystemWatcher` ハンドラは即 return
- ハンドラ内では `WatcherEvent` を `Channel.Writer.TryWrite` するだけにする
- ハンドラ内で DB・I/O・UI 更新を実行しない

2. ファイル準備待ちは `WaitFileReadyAsync` へ集約
- `Thread.Sleep` を廃止し、`await Task.Delay(...)` でリトライ
- 最大試行回数・待機間隔・キャンセル対応を明示

3. DB反映は Processor 側で非同期実行
- `System.Data.SQLite` が同期 API 中心のため、まずは `Task.Run` で隔離
- 将来は DB 層自体を async 対応プロバイダへ差し替え可能な境界にする

4. UI反映は Dispatcher ブリッジ経由に固定
- `DataRowToViewData` / 一覧更新は `WatcherUiBridge` からのみ呼ぶ
- Processor から直接 UI コレクションへ触らない

5. サムネイル投入は重複抑止を維持
- 既存 `TryEnqueueThumbnailJob` を利用
- イベント多重時は Processor 側で path/movie_id 単位のデバウンスを追加

## 6. 実装フェーズ

### Phase 1: 土台作成（分離）
- `Watcher` フォルダにイベント DTO / Channel / RegistrationService を追加
- `MainWindow` から監視登録ロジックを移設
- 既存挙動を変えず、イベントを Channel に流すところまで切り替え

### Phase 2: Created/Renamed の非同期プロセッサ実装
- `WatcherEventProcessor` を常駐タスクで起動
- `Created` は `WaitFileReadyAsync` -> DB登録 -> UI反映 -> サムネ投入
- `Renamed` は DB更新 -> サムネ/ブックマーク名更新を非同期化
- 例外はイベント単位で握り、アプリ全体停止を避ける

### Phase 3: 既存コード置換と責務縮小
- `MainWindow.Watcher.cs` は起動/停止の委譲だけにする
- 監視処理本体を `Watcher` フォルダ実装へ一本化
- 不要になった同期処理を削除（`Thread.Sleep` など）

### Phase 4: 負荷対策と安定化
- `Channel` を `BoundedChannel` 化して過負荷時の方針を決定
- ログに「投入数/処理数/ドロップ数/遅延」を追加
- 大量コピー時の処理遅延と UI 応答性を測定

## 7. 具体タスク（順序付き）
1. `Watcher` フォルダ新設とクラス雛形追加
2. `WatcherEvent`（種別・パス・旧パス・発生時刻）定義
3. `WatcherRegistrationService` で `FileSystemWatcher` 管理を移設
4. `WatcherEventChannel`（`Channel<WatcherEvent>`）導入
5. `WatcherEventProcessor` 常駐タスク起動/停止を `MainWindow` に接続
6. `WaitFileReadyAsync` 実装（非同期リトライ）
7. DB処理を `WatcherDbGateway` に分離し `Task.Run` で隔離
8. UI更新を `WatcherUiBridge` に分離
9. `RenameThumb` 経路を `Task` ベースに改修（`async void` 依存を削減）
10. 監視経路のログ整備と例外方針統一

## 8. 完了条件
- 監視イベントハンドラ内に `Thread.Sleep` と同期 DB 呼び出しが残っていない
- 監視イベントからサムネイル投入までが `Task`/`Channel` ベースで接続されている
- `MainWindow` 側の監視責務が「起動/停止/依存注入」中心になっている
- Created/Renamed の主要フローで回帰（DB反映・UI反映・サムネ生成）がない

## 9. リスクと対策
- リスク: イベント大量発生でメモリが増える
  - 対策: `BoundedChannel` + デバウンス + 重複排除
- リスク: 同期 DB API によるスレッドプール圧迫
  - 対策: DB 実行数を `SemaphoreSlim` で制限（例: 2〜4）
- リスク: UI スレッド境界ミス
  - 対策: UI 変更を `WatcherUiBridge` 1か所に限定

## 10. 検証項目
1. 新規動画コピー直後の登録とサムネイル生成が完走する
2. 連続100件追加時に UI フリーズしない
3. リネーム時に movie / bookmark / サムネイル名の整合が取れる
4. アプリ終了時に Processor がキャンセルされ、例外を残さない

## 11. 実施目安
- Phase 1: 0.5日
- Phase 2: 1.5日
- Phase 3: 1日
- Phase 4: 0.5日
- 合計: 3.5日（レビュー/手動試験込み）

## 12. 実施記録（2026-02-24）

### 12.1 Thumbnail連携の進捗同期（反映済み）
- Watcher からのサムネイル投入は `TryEnqueueThumbnailJob` 経由に統一済み（`MainWindow.Watcher.cs`）。
- `TryEnqueueThumbnailJob` は runtime queue 直接投入ではなく、`QueueRequest` Producer として QueueDB 永続化経路へ接続済み。
- Consumer 側は QueueDB リース中心へ移行済みのため、Watcher 経路の投入先も DB 正で処理される。

### 12.2 仕様同期（Thumbnail 側と整合）
- タブ切替時は既存DBキューを保持する（破棄しない）。
- DB切替時も既存DBキューは保持し、Consumer は「現在開いているDB」のみ処理する。
- ユーザーが選択中タブのジョブを優先消化する（`preferredTabIndex` による優先リース）。

### 12.3 Watcher分離フェーズの現状
- Phase 1（Watcherフォルダ分離）: 未着手
- Phase 2（Created/Renamed Processor 化）: 未着手
- Phase 3（MainWindow から責務分離）: 未着手
- Phase 4（BoundedChannel/運用ログ）: 未着手

### 12.4 現時点の残課題（次着手対象）
- `FileChanged` は依然としてイベントハンドラ内で `Thread.Sleep` を使用している（`WaitFileReadyAsync` へ未移行）。
- `FileChanged` はイベントハンドラ内で DB 更新と UI 反映を直接実行している（Channel + Processor 分離未実施）。
- 例外時に `Application.Current.Shutdown()` でアプリ終了する挙動が残っている（イベント単位失敗へ未切替）。
