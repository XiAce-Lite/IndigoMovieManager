# 設計メモ: FileIndexProvider異常とサムネイル高負荷ログ分離（2026-03-07）

## 1. 目的
- `usnmft` の `AdminRequired` や `availability_error:*` を、サムネイルの `high-load` / `fallback` と混同しない。
- FileIndexProvider 側の異常は `file-index-*` 軸へ固定し、サムネイル高負荷制御は `high-load` 軸のまま維持する。

## 2. 結論
- FileIndexProvider の reason は `FileIndexReasonTable` で `reason_category` と `log axis` へ正規化する。
- `log axis` は `file-index-ok` / `file-index-availability` / `file-index-query` / `file-index-thumb-query` / `file-index-capacity` / `file-index-eligibility` / `file-index-unknown` を固定値にする。
- `AdminRequired` は `file-index-availability` へ分類し、サムネイル高負荷や管理者権限テレメトリ劣化とは別軸に残す。

## 3. 適用箇所
- `CreateWatcher` の可用性ログ
- `scan strategy` の戦略ログ
- `BuildExistingThumbnailBodySet` の Everything / filesystem 切替ログ

## 4. 境界
- FileIndexProvider の可用性問題
  - `file-index-*`
- 管理者権限テレメトリの接続/権限/タイムアウト問題
  - `fallback_kind=*`
- サムネイル並列制御の抑制/復帰
  - `category=high-load` / `category=error`

## 5. 受け入れ基準
- `availability_error:AdminRequired` が `file-index-availability` として追跡できる。
- `thumb queue summary` や `admin telemetry state` の `high-load` / `fallback` 軸へ FileIndexProvider reason を流し込まない。
- Watcher のログだけ見ても、FileIndexProvider の可用性問題とサムネイル負荷制御を見分けられる。
