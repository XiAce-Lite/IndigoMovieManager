# MovieInfo を FFMediaToolkit へ切替える場合の影響範囲とベンチ（2026-02-25）

## 1. 結論（要点）
- `MovieInfo` の OpenCV 依存は **メタ情報取得（FPS / フレーム数 / 尺）** に限定される。
- 切替影響の中心は `Models/MovieInfo.cs` と、それを呼ぶ監視登録経路。
- 実測では、メタ情報取得は FFMediaToolkit が OpenCvSharp より大幅に高速だった。
- 絵文字入力では OpenCvSharp が失敗し、FFMediaToolkit は成功した。

## 2. 影響範囲（コード）

### 2.1 直接影響（要修正）
- `Models/MovieInfo.cs`
  - 現在 `VideoCapture` で `TotalFrames` / `FPS` を取得して `MovieLength` を算出。
  - ここを FFMediaToolkit (`MediaFile.Open`) ベースへ置換する。

### 2.2 呼び出し側（挙動確認対象）
- `MainWindow.Watcher.cs`
  - `new MovieInfo(movieFullPath)` 実行後に DB登録 (`InsertMovieTable`) へ流す経路。
  - 取得値（`MovieLength` 等）の互換が必要。

### 2.3 DB投入側（互換確認対象）
- `DB/SQLite.cs`
  - `InsertMovieTable(string, MovieInfo)` → `ToMovieCore()` で登録。
  - DBへ保存する主要項目は `movie_length`, `hash`, `movie_size`。
  - `FPS` / `TotalFrames` は DB未保存だが、`MovieCore` では保持されるため値の整合は維持したい。

### 2.4 非影響（今回の切替対象外）
- サムネイル本体生成（`Thumbnail/ThumbnailCreationService.cs`）
  - こちらは別経路（既存OpenCV + ffmpegフォールバック）で動作。

## 3. 互換要件（切替時に守る項目）
- `MovieInfo.MovieLength` は秒単位（long）で従来互換。
- `MovieInfo.FPS` は `<=0` 時に 30 フォールバックの従来仕様を維持。
- `MovieInfo.TotalFrames` は取得不能時の扱いを明示（推定または0）。
- `MoviePath` は生パス保持のまま（DB/UI互換）。
- ハッシュ計算 (`Tools.GetHashCRC32`) は現行維持。

## 4. メタ情報取得ベンチ（MovieInfo相当）

### 4.1 ベンチ条件
- プロジェクト: `C:\Users\%USERNAME%\source\repos\IMM_Lab\MovieInfoMetaBench`
- 取得項目: FPS / TotalFrames / DurationSec
- 反復回数: 300回
- 入力:
  - ASCII: `...\MovieInfoMetaBenchOutput\meta_source.mp4`
  - Emoji: `...\MovieInfoMetaBenchOutput\📁入力😀\動画🎬メタ情報.mp4`

### 4.2 実測結果
- ASCII
  - FFMediaToolkit: `2.2110 ms/call`（成功）
  - OpenCvSharp: `94.2267 ms/call`（成功）
- Emoji
  - FFMediaToolkit: `1.4857 ms/call`（成功）
  - OpenCvSharp: 失敗（動画オープン失敗）

### 4.3 値の一致
- 成功ケース（ASCII）では、両方式とも以下で一致。
  - FPS: `30.0000`
  - TotalFrames: `720`
  - DurationSec: `24.0000`

## 5. リスクと対策
- リスク: FFMediaToolkit の DLL 構成不整合（7.x shared以外）で初期化失敗。
  - 対策: `FFmpegLoader.LoadFFmpeg()` の失敗ログを明示し、フォールバックを用意。
- リスク: 一部コーデックで `NumberOfFrames` が未提供。
  - 対策: `Duration * FPS` 推定の補完ロジックを実装。
- リスク: 切替時の監視スループット変動。
  - 対策: `MainWindow.Watcher` で処理時間ログを比較して導入前後を検証。

## 6. 推奨導入ステップ
1. `MovieInfo` のみ FFMediaToolkit へ切替（最小差分）。
2. 失敗時のみ既存OpenCVへフォールバック（段階導入）。
3. 監視登録（新規ファイル大量投入）で回帰確認。
4. 問題なければ OpenCV依存のメタ取得部分を縮退。

## 7. 参考ログ
- `C:\Users\%USERNAME%\source\repos\IMM_Lab\MovieInfoMetaBenchOutput\movieinfo_meta_bench_log.txt`
