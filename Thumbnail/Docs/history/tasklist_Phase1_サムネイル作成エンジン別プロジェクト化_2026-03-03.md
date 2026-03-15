# tasklist（Phase 1: サムネイル作成エンジン別プロジェクト化 2026-03-03）

## 0. 使い方
- このタスクリストは上から順に実行する。
- 各タスクは「完了条件」を満たした時点でチェックを付ける。
- 依存タスクが未完了のまま次へ進まない。

## 1. タスク一覧

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| P1-001 | 未着手 | 新規プロジェクト `src/IndigoMovieManager.Thumbnail.Engine` 作成 | `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj` | csprojが作成され、必要PackageReferenceが定義されている |
| P1-002 | 未着手 | Engine用の基盤インターフェース追加（logger/meta） | `src/.../Abstractions/*.cs` | `IVideoMetadataProvider`, `IThumbnailLogger` が定義される |
| P1-003 | 未着手 | `QueueObj`/`TabInfo`/`ThumbInfo` 移設 | `src/.../Models/*.cs` | 旧参照を壊さずに新namespaceでコンパイル可能 |
| P1-004 | 未着手 | `ThumbInfo` から `MessageBox` 依存除去 | `src/.../Models/ThumbInfo.cs` | WPF名前空間参照が消えている |
| P1-005 | 未着手 | `Tools`/`PathResolver`/`EnvConfig`/`ParallelController` 移設 | `src/.../Core/*.cs` | ユーティリティ群が新プロジェクト内で解決する |
| P1-006 | 未着手 | `Engines`/`Decoders` 移設 | `src/.../Engines/*.cs`, `src/.../Decoders/*.cs` | 4エンジンの型解決が通る |
| P1-007 | 未着手 | `ThumbnailCreationService` 移設 | `src/.../ThumbnailCreationService.cs` | サービス本体が新プロジェクトで成立する |
| P1-008 | 未着手 | `MovieInfo` 依存を `IVideoMetadataProvider` 経由に置換 | `src/.../ThumbnailCreationService.cs` | `new MovieInfo(...)` が消える |
| P1-009 | 未着手 | `DebugRuntimeLog` 依存を `IThumbnailLogger` 経由に置換 | Engine配下全体 | Engine側から `DebugRuntimeLog` 参照が消える |
| P1-010 | 未着手 | App側Adapter追加（MovieInfo/DebugRuntimeLogブリッジ） | `IndigoMovieManager_fork` 側の新規Adapterファイル | Engineへ必要依存を注入できる |
| P1-011 | 未着手 | `MainWindow` のサービス生成を新Engine参照へ変更 | `MainWindow.xaml.cs` ほか | 既存操作で `ThumbnailCreationService` が新実装を使う |
| P1-012 | 未着手 | 本体csprojにProjectReference追加 | `IndigoMovieManager_fork.csproj` | Engineプロジェクト参照が追加される |
| P1-013 | 未着手 | 旧 `Thumbnail` 配下の重複コードを整理/削除 | 旧対象ファイル群 | 二重定義エラーが発生しない |
| P1-014 | 未着手 | テストプロジェクトの参照追従 | `Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj` | テストが新Engine側型を参照できる |
| P1-015 | 未着手 | 回帰確認（形式互換・エンジン切替） | 手動確認メモ/必要に応じてテスト修正 | 互換項目が全てOKになる |

## 2. チェックリスト（実装者向け）
- [ ] EngineプロジェクトにWPF参照が混入していない。
- [ ] Engineプロジェクトから `IndigoMovieManager` ルート名前空間参照が消えている。
- [ ] `ThumbInfo` で `System.Windows` を参照していない。
- [ ] `ThumbnailCreationService` で `MovieInfo` を直接生成していない。
- [ ] Engine関連テストが新しい参照構成で解決している。
- [ ] 既存の出力ファイル名規則 `動画名.#hash.jpg` を維持している。
- [ ] WhiteBrowser互換メタ（SecBuffer/InfoBuffer）を維持している。

## 3. 受け入れ観点（最終）
- [ ] 手動サムネイル作成が成功する。
- [ ] 通常サムネイル作成（キュー経由）が成功する。
- [ ] エンジン強制切替（`IMM_THUMB_ENGINE`）が有効。
- [ ] 失敗時フォールバック順が維持される。
- [ ] サムネイル生成結果CSVの形式が維持される。

## 4. メモ
- Phase 1では QueueDb / QueuePipeline / QueueProcessor は移設しない。
- Phase 2でキュー分離と進捗通知抽象化を行う。