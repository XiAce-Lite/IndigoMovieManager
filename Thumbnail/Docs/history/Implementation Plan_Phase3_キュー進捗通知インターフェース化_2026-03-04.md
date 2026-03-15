# Implementation Plan（Phase 3: キュー進捗通知インターフェース化 2026-03-04）

## 1. 目的
- `IndigoMovieManager.Thumbnail.Queue` から `Notification.Wpf` 依存を除去し、UI非依存のライブラリ境界を完成させる。
- MainWindow から見える挙動（進捗バー表示、処理中メッセージ、終了時クローズ）を維持する。
- Queue実装を「処理ロジック」と「表示実装」に分離し、将来のCLI/別UI再利用を可能にする。

## 2. スコープ
- IN
  - `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - `src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj`
  - `src/IndigoMovieManager.Thumbnail.Queue/Abstractions/*.cs`（新規）
  - `Thumbnail/Adapters/*.cs`（App側の通知Adapterを追加）
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`（Queue実行時の注入）
  - `Tests/IndigoMovieManager_fork.Tests/*Queue*.cs`（必要最小の追従）
- OUT
  - QueueDBスキーマ、リース制御、再試行ロジックの仕様変更
  - サムネイル生成アルゴリズムの変更

## 3. 設計方針
- Queue側に通知ポートを定義する。
  - `IThumbnailQueueProgressPresenter`
  - `IThumbnailQueueProgressHandle : IDisposable`
- `ThumbnailQueueProcessor` は `Notification.Wpf` を直接使わず、ポート経由で `Report` / `Dispose` する。
- 既定動作は NoOp 実装を使い、注入なしでも処理継続できるようにする。
- App側で `Notification.Wpf` を使うAdapterを実装し、MainWindowから注入する。

## 4. 実装ブロック
- [x] P3-001: Queue側に進捗通知インターフェース（Presenter/Handle）を追加
- [x] P3-002: Queue側に NoOp 実装を追加（注入省略時の既定）
- [x] P3-003: `ThumbnailQueueProcessor.RunAsync` を通知ポート受け取りへ変更
- [x] P3-004: `ThumbnailQueueProcessor` から `Notification.Wpf` 直接参照を除去
- [x] P3-005: Queue csproj から `Notification.Wpf` PackageReference を削除
- [x] P3-006: App側に `Notification.Wpf` Adapter を実装
- [x] P3-007: MainWindowのQueue起動経路でAdapterを注入
- [x] P3-008: テスト追従（モック/NoOpを使った最小更新）
- [x] P3-009: ビルド/テストを実施
- [ ] P3-010: 手動回帰を実施（進捗表示・キャンセル・再起動）

## 5. 完了条件（Phase 3 DoD）
- `src/IndigoMovieManager.Thumbnail.Queue` が `Notification.Wpf` 参照なしでビルドできる。
- 本体アプリで進捗バー表示とクローズ挙動が従来どおり動作する。
- Queue処理（投入→処理→完了/失敗）の挙動に退行がない。
- `MSBuild` と `dotnet test --no-build` が通る。

## 6. 検証コマンド
- Queue単体:
  - `dotnet build src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj -c Debug`
- 本体:
  - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- テスト:
  - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /p:UseSharedCompilation=false /m:1`
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --no-build`

## 7. リスクと対策
- リスク: 通知境界追加で進捗表示タイミングがずれる。
  - 対策: 既存 `Report` 呼び出し箇所は変えず、呼び先だけ差し替える。
- リスク: 例外時に通知ハンドルが解放されない。
  - 対策: `IThumbnailQueueProgressHandle` を `using`/`finally` で必ず `Dispose`。
- リスク: UIスレッド制約でAdapter側が例外化する。
  - 対策: Adapter内で例外を吸収し、Queue本体へ波及させない。

## 8. 実装順の推奨
1. Queue側ポート追加（P3-001, P3-002）
2. QueueProcessor置換（P3-003, P3-004, P3-005）
3. App Adapter注入（P3-006, P3-007）
4. テスト/回帰（P3-008, P3-009, P3-010）

## 9. 実行結果メモ（2026-03-04）
- Queue単体ビルド: 成功（0警告 / 0エラー）
- 本体ビルド（MSBuild, Debug|x64）: 成功（0警告 / 0エラー）
- テストビルド（MSBuild, Debug|x64）: 成功（0警告 / 0エラー）
- テスト実行（`dotnet test --no-build`）: 合格 45 / 失敗 0 / スキップ 2
- 手動回帰（進捗表示・キャンセル・再起動）: 未実施
