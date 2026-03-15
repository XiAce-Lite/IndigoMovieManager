# Implementation Plan + tasklist（サムネイル進捗ミニパネル WriteableBitmapプレビュー直結 2026-03-04）

## 0. 背景
- 現状は「JPEG保存 -> WPF側でファイル再読込」の経路になっており、ミニパネル反映が遅れやすい。
- `autogen` は内部でフレームを既にメモリ展開しているため、プレビューだけ先にUIへ渡せる余地がある。
- ただしエンジン層へWPF依存は持ち込まない（DLL分離方針を維持）。
- QueueDB拡張子は `*.queue.db` から `*.queue.imm` へ変更済み。本計画は表示経路が主対象で、拡張子変更そのものは扱わない。

## 1. 目的
- サムネイル保存完了を待たず、ミニパネルへ先行表示できる構成を作る。
- エンジン層はUI非依存のまま維持する。
- 既存JPEG出力（WhiteBrowser互換）を壊さない。

## 2. スコープ
- IN
  - `ThumbnailCreateResult` にプレビュー用中立データを追加
  - `autogen` からプレビュー候補フレームを返却
  - App側で `WriteableBitmap` 化してミニパネルへ反映
  - 同一パス更新時の再描画漏れ防止（revision導入）
- OUT
  - サムネイル保存フォーマット変更
  - QueueDBスキーマ変更 / QueueDB拡張子変更（`*.queue.imm`）
  - `ffmpeg1pass` / `opencv` / `ffmediatoolkit` の全面刷新

## 3. 設計方針
- エンジン側は `byte[] + width + height + stride + pixelFormat` の中立DTOを返す。
- WPF側だけで `WriteableBitmap.WritePixels` を使って `ImageSource` を作る。
- ミニパネルは「メモリプレビュー優先、未取得時は従来ファイルパス表示」にする。
- 同一動画・同一パスの再生成を拾うため、`PreviewRevision` を加算して再評価を保証する。

## 4. フェーズ

### Phase 1: 契約追加（UI非依存）
- `ThumbnailCreateResult` に `PreviewFrame`（nullable）を追加する。
- `PreviewFrame` はUI参照を含まない中立型で定義する。
- メモリ上限を超えるフレームは縮小して保持する（ミニパネル用途）。

### Phase 2: autogen実装
- `FfmpegAutoGenThumbnailGenerationEngine` で取得済みフレームからプレビュー候補を抽出する。
- JPEG保存処理は現状維持しつつ、結果DTOへプレビューを詰める。
- プレビュー抽出失敗時は `PreviewFrame=null` で継続する。

### Phase 3: App側WriteableBitmap反映
- App側に `ThumbnailPreviewCache`（キー+revision管理）を追加する。
- `CreateThumbAsync` で `result.PreviewFrame` を受け取り、UIスレッドで `WriteableBitmap` 化してキャッシュへ登録する。
- `ThumbnailProgressRuntime` / `ThumbnailProgressViewState` に `PreviewKey` と `PreviewRevision` を通す。

### Phase 4: XAML接続とフォールバック
- ミニパネル画像バインドを「キャッシュ参照 + 既存パス fallback」へ変更する。
- 同一パス更新時も `PreviewRevision` 変化で再描画されることを保証する。

### Phase 5: 計測と安定化
- 「保存完了 -> ミニパネル表示」の遅延をログ化する。
- 既存 `NoLockImageConverter` はfallback専用として軽量化（`DecodePixelHeight`）を追加する。

## 5. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| WB-001 | 完了 | プレビュー中立DTOを定義する | `Thumbnail/ThumbnailCreationService.cs` | `ThumbnailCreateResult` に `PreviewFrame` を追加し、既存呼び出しがコンパイル通過 |
| WB-002 | 完了 | DTO生成ヘルパーを追加する | `Thumbnail/ThumbnailCreationService.cs` | 成功/失敗結果生成で `PreviewFrame` を安全に設定できる |
| WB-003 | 完了 | autogenでプレビュー候補フレームを抽出する | `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs` | 生成成功時に `PreviewFrame` が返る（失敗時はnullで継続） |
| WB-004 | 完了 | 先行表示の画素サイズを固定する | `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs` | ミニパネル想定サイズへ縮小済みで返却される |
| WB-005 | 完了 | App側プレビューキャッシュを実装する | `Thumbnail/Adapters/ThumbnailPreviewCache.cs`（新規） | キー+revisionで `ImageSource` を取得/破棄できる |
| WB-006 | 完了 | CreateThumbAsyncでプレビュー受け渡しを接続する | `Thumbnail/MainWindow.ThumbnailCreation.cs` | `result.PreviewFrame` を受けてキャッシュへ登録し、例外時も処理継続 |
| WB-007 | 完了 | Runtime/Snapshotへ preview key/revision を追加する | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressRuntime.cs` | `WorkerSnapshot` が `PreviewKey`/`PreviewRevision` を保持する |
| WB-008 | 完了 | ViewStateへ preview key/revision を反映する | `ModelViews/ThumbnailProgressViewState.cs` | `WorkerPanel` が `PreviewKey`/`PreviewRevision` を公開する |
| WB-009 | 完了 | ミニパネル画像バインドをキャッシュ優先へ変更する | `MainWindow.xaml` `Converter/*` | ミニパネル表示がメモリプレビュー優先、未取得時は既存パスfallback |
| WB-010 | 完了 | 同一パス更新の再描画漏れを解消する | `ModelViews/ThumbnailProgressViewState.cs` `MainWindow.xaml` | 同じ `PreviewImagePath` でも revision増分で更新される |
| WB-011 | 完了 | fallbackデコードを軽量化する | `Converter/NoLockImageConverter.cs` | ミニパネル用途で `DecodePixelHeight` が有効 |
| WB-012 | 完了 | 遅延計測ログを追加する | `Thumbnail/MainWindow.ThumbnailCreation.cs` `logs/*` | P50/P95が採取できるCSVが出力される |
| WB-013 | 完了 | 単体テストを追従する | `Tests/IndigoMovieManager_fork.Tests/*` | DTO互換・revision更新・fallback経路のテスト追加 |
| WB-014 | 未着手 | 手動回帰（高負荷時）を実施する | 手順書（本書 7章） | 表示遅延とUI操作遅延が受け入れ基準を満たす |

## 5.1 実行メモ（2026-03-04）
- `MSBuild.exe IndigoMovieManager_fork.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1` : 成功（0警告 / 0エラー）
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug` : 成功（49合格 / 0失敗 / 2スキップ）

## 6. 受け入れ基準
- ミニパネル表示遅延（保存完了 -> 表示反映）
  - P95 <= 800ms
  - Max <= 2000ms（I/O異常時を除く）
- 同一動画を連続再生成しても、古い画像が残留しない。
- エンジンプロジェクトに `System.Windows.*` 依存が追加されない。
- 既存JPEG保存結果（ファイル形式/末尾メタ情報）に退行がない。

## 7. 手動回帰手順（最小）
1. サムネイル進捗タブを開いた状態で100件以上を連続投入する。
2. 作成中にミニパネル画像が順次更新されることを確認する。
3. 同一動画の再作成を連続実行し、画像が差し替わることを確認する。
4. CPU負荷が高い状態で一覧操作（選択/スクロール）が詰まらないことを確認する。
5. 失敗動画（破損/DRM疑い）で処理継続し、fallback表示が崩れないことを確認する。

## 8. リスクと対策
- リスク: プレビュー用バッファでメモリ増加
  - 対策: ミニサイズ固定、キャッシュ件数上限、古いentryをLRUで破棄
- リスク: スレッド境界で `WriteableBitmap` 例外
  - 対策: UIスレッドで `WritePixels` 実行、完成後 `Freeze` して保持
- リスク: autogen以外エンジンとの挙動差
  - 対策: `PreviewFrame` はoptionalにし、未対応エンジンは既存fallback表示を使う
