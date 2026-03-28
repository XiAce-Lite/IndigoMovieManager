# tasklist（Phase 3: キュー進捗通知インターフェース化 2026-03-04）

## 0. 使い方
- 上から順に実施する。
- 各タスクは完了条件を満たした時点で `状態` を更新する。
- 依存タスクが未完了のまま次へ進まない。

## 1. タスク一覧

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| P3-001 | 完了 | Queue側通知ポート（Presenter/Handle）を定義する | `src/IndigoMovieManager.Thumbnail.Queue/Abstractions/*.cs` | `IThumbnailQueueProgressPresenter` と `IThumbnailQueueProgressHandle` が作成される |
| P3-002 | 完了 | Queue側のNoOp通知実装を追加する | `src/IndigoMovieManager.Thumbnail.Queue/Abstractions/NoOp*.cs` | 注入なしでも例外なく動作する |
| P3-003 | 完了 | `RunAsync` に通知ポート引数を追加する | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | 既存呼び出し互換を保ってビルドできる |
| P3-004 | 完了 | QueueProcessor内の通知処理をポート経由へ置換する | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | `NotificationManager` 直利用が消える |
| P3-005 | 完了 | Queue csprojから `Notification.Wpf` を外す | `src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj` | Queue単体が `Notification.Wpf` なしでビルド成功 |
| P3-006 | 完了 | App側通知Adapterを追加する | `Thumbnail/Adapters/AppThumbnailQueueProgress*.cs` | Adapterが `Notification.Wpf` を閉じ込めている |
| P3-007 | 完了 | MainWindowのQueue起動でAdapterを注入する | `Thumbnail/MainWindow.ThumbnailCreation.cs` `MainWindow.xaml.cs` | 進捗表示が従来どおり表示/終了する |
| P3-008 | 完了 | テストを新境界に追従する | `Tests/IndigoMovieManager_fork.Tests/*Queue*.cs` | Queue関連テストがビルド通過 |
| P3-009 | 完了 | ビルド/テストを直列で確認する | `IndigoMovieManager_fork.csproj` `Tests/*.csproj` | MSBuild + `dotnet test --no-build` 成功 |
| P3-010 | 未着手 | 手動回帰（進捗表示・キャンセル・再起動）を確認する | 手順書（別紙） | 退行なしを記録できる |

## 2. チェックリスト
- [x] Queueプロジェクト内に `using Notification.Wpf;` が残っていない。
- [x] Queue csprojに `Notification.Wpf` の `PackageReference` が残っていない。
- [ ] 進捗通知で例外が発生してもQueue本体は継続する。
- [ ] キャンセル時に通知ハンドルがリークしない。
- [ ] MainWindowで進捗バーの表示/更新/閉じるが維持される。

## 3. 受け入れ観点（最終）
- [x] Queue単体ビルド成功（WPF依存なし）。
- [x] 本体ビルド成功（Debug|x64）。
- [x] テスト成功（`dotnet test --no-build`）。
- [ ] 通常キュー投入で進捗UIが表示され、完了で閉じる。
- [ ] 失敗ジョブ/再試行動作に退行がない。
