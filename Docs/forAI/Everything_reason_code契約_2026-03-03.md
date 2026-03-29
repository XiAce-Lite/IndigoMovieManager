# Everything reasonコード契約（2026-03-03 / 更新 2026-03-04）

## 1. 目的
- Everything連携の失敗/成功理由を、実装差し替え後も同じ意味で扱えるように契約化する。
- `MainWindow.Watcher` 側の通知解釈ロジックと互換を維持する。

## 2. 契約ルール
- 既存reasonコードは**文字列互換固定**とする。
- reasonは英小文字 + `_` を基本とし、動的値は `:` 以降へ付与する。
- UI文言はreasonからホスト側で解釈する。Provider側は文言を返さない。
- unknownなreasonはホスト側で「不明理由フォールバック」として扱う。
- `ok:` 以降の付帯キー（例: `query_count=...`, `provider=everythinglite`, `index=...`, `indexed_at=...`）は実装差分を許容し、Prefix互換を優先する。

## 3. reasonコード一覧（固定）

### 3.1 可用性・設定
- `setting_disabled`
- `auto_not_available`
- `everything_not_available`
- `availability_error:{ExceptionType}`

### 3.2 検索実行（動画）
- `ok:query_count={N}`
- `ok:query_count={N} since={UtcIso8601}`
- `ok:provider=everythinglite count={N}`
- `ok:provider=everythinglite count={N} since={UtcIso8601}`
- `ok:provider=everythinglite index=rebuilt indexed_at={UtcIso8601} count={N}`
- `ok:provider=everythinglite index=cached indexed_at={UtcIso8601} count={N}`
- `ok:provider=everythinglite index=rebuilt indexed_at={UtcIso8601} count={N} since={UtcIso8601}`
- `ok:provider=everythinglite index=cached indexed_at={UtcIso8601} count={N} since={UtcIso8601}`
- `everything_result_truncated:{NumItems}/{TotalItems}`
- `everything_query_error:{ExceptionType}`

### 3.3 検索実行（サムネイル）
- `ok`
- `everything_result_truncated:{NumItems}/{TotalItems}`
- `everything_thumb_query_error:{ExceptionType}`

### 3.4 経路適格性（path判定）
- `path_not_eligible:empty_path`
- `path_not_eligible:unc_path`
- `path_not_eligible:no_root`
- `path_not_eligible:drive_type_{DriveType}`
- `path_not_eligible:drive_format_{DriveFormat}`
- `path_not_eligible:eligibility_error:{ExceptionType}`
- `path_not_eligible:ok`

## 4. 判定規則（Prefix一致）
- 固定一致で扱う:
  - `setting_disabled`
  - `auto_not_available`
  - `everything_not_available`
  - `ok`
- Prefix一致で扱う:
  - `ok:`
  - `availability_error:`
  - `everything_query_error:`
  - `everything_thumb_query_error:`
  - `everything_result_truncated:`
  - `path_not_eligible:`

## 5. 互換ポリシー
- 既存reasonの削除・改名は禁止。
- 新規reason追加は許可。ただし既存Prefixの意味を壊さない。
- 新規reason追加時は本契約書とフォールバック条件表を同時更新する。

## 6. A/B差分テスト時の判定ルール
- `reason` は完全一致ではなく、以下のカテゴリ一致で比較する。
  - 固定値: `ok`, `setting_disabled`, `auto_not_available`, `everything_not_available`
  - Prefix: `ok:`, `availability_error:`, `everything_query_error:`, `everything_thumb_query_error:`, `everything_result_truncated:`
- `everything` と `everythinglite` で検索エンジンの観測タイミング差が出る場合、件数比較は環境依存としてスキップを許容する。

## 7. 出典
- `Watcher/EverythingFolderSyncService.cs`
- `Watcher/MainWindow.Watcher.cs` (`IsEverythingEligiblePath`, `DescribeEverythingDetail`)
- `Watcher/EverythingLiteProvider.cs`
- `Tests/IndigoMovieManager.Tests/FileIndexProviderAbDiffTests.cs`
