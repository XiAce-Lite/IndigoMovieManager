# EverythingLite内包境界定義（2026-03-05）

## 1. 目的
- `MyLab/EverythingLite` から取り込む範囲を固定し、再分離可能な境界を維持する。
- 内包後の命名を技術名ベース（`UsnMft`）へ統一し、名称の混乱を防ぐ。

## 2. 命名ルール
- 内包プロジェクト名: `IndigoMovieManager.FileIndex.UsnMft`
- 内包名前空間: `IndigoMovieManager.FileIndex.UsnMft`
- 互換キー:
  - 設定キー `everythinglite` は既存互換のため維持する。
  - `EverythingLiteProvider` クラス名も既存互換のため当面維持する。

## 3. 含有範囲（IN）
- 取り込み対象（コア層）
  - `AdminUsnMftIndexBackend.cs`
  - `AppStructuredLog.cs`
  - `FileIndexService.cs`
  - `FileIndexServiceOptions.cs`
  - `IFileIndexService.cs`
  - `IIndexBackend.cs`
  - `IndexProgress.cs`
  - `SearchResultItem.cs`
  - `StandardFileSystemIndexBackend.cs`

## 4. 除外範囲（OUT）
- UI/アプリ層は取り込まない
  - `Program.cs`
  - `MainForm.cs`
  - `AppUserSettingsStore.cs`
  - `App.config`
  - `Properties/AssemblyInfo.cs`
  - `EverythingLite.csproj`（外部側のWinFormsプロジェクト定義）

## 5. ファイル対応表
| 外部（MyLab） | 内包（IndigoMovieManager_fork） |
|---|---|
| `MyLab/EverythingLite/AdminUsnMftIndexBackend.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/AdminUsnMftIndexBackend.cs` |
| `MyLab/EverythingLite/AppStructuredLog.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/AppStructuredLog.cs` |
| `MyLab/EverythingLite/FileIndexService.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/FileIndexService.cs` |
| `MyLab/EverythingLite/FileIndexServiceOptions.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/FileIndexServiceOptions.cs` |
| `MyLab/EverythingLite/IFileIndexService.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/IFileIndexService.cs` |
| `MyLab/EverythingLite/IIndexBackend.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/IIndexBackend.cs` |
| `MyLab/EverythingLite/IndexProgress.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/IndexProgress.cs` |
| `MyLab/EverythingLite/SearchResultItem.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/SearchResultItem.cs` |
| `MyLab/EverythingLite/StandardFileSystemIndexBackend.cs` | `src/IndigoMovieManager.FileIndex.UsnMft/StandardFileSystemIndexBackend.cs` |

## 6. 同期手順
- 取り込み（外部 -> 内包）
  - `pwsh -NoProfile -File .\scripts\everythinglite_sync.ps1 -Mode Import`
- 逆同期（内包 -> 外部）
  - `pwsh -NoProfile -File .\scripts\everythinglite_sync.ps1 -Mode Export`

## 7. 運用ルール
- 同期時は「ロジック変更」と「同期コミット」を分離する。
- 参照切替は `UseExternalEverythingLite` で制御する。
  - 既定: `false`（内包 `UsnMft`）
  - 外部利用: `true` + `ExternalEverythingLiteProjectPath` 指定
- PRレビューでは `src/IndigoMovieManager.FileIndex.UsnMft` と `scripts/everythinglite_sync.ps1` の整合をセットで確認する。
