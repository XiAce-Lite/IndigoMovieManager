# Implementation Plan（MovieInfo の FFMediaToolkit 切替）

## 1. 目的
- `MovieInfo` のメタ情報取得を OpenCvSharp から FFMediaToolkit へ段階切替し、絵文字パス互換と登録速度を改善する。
- 既存DB（WhiteBrowser互換）は変更しない。
- 問題発生時に即時ロールバックできる構成で導入する。
- 絵文字問題の完全解決

## 2. 精査結果（現状）
- 直接改修点は `Models/MovieInfo.cs`（OpenCVで `FPS / FrameCount / MovieLength` を取得）。
- 呼び出し側は `Watcher/MainWindow.Watcher.cs` の2経路。
  - `FileChanged` 内: `new MovieInfo(e.FullPath)`
  - `CheckFolderAsync` 内: `new MovieInfo(movieFullPath)`
- DB投入は `DB/SQLite.cs` の `InsertMovieTable(string, MovieInfo)` で `MovieCore` 化して登録。
- `FPS` / `TotalFrames` はDB保存されないが、`MovieCore` で保持される。
- 現在のプロジェクトには FFMediaToolkit 依存と FFmpeg shared DLL 配置が未整備。
- `tools/ffmpeg` には `ffmpeg.exe` 系はあるが、`avcodec*.dll` 等の shared DLL は存在しない。

## 3. 互換要件（固定）
- `MovieInfo.MovieLength` は秒単位 `long`（従来どおり端数切り捨て）。
- `MovieInfo.FPS <= 0` の場合は `30` へフォールバック。
- `MovieInfo.TotalFrames` が未取得時は `Duration * FPS` で推定し、不可なら `0`。
- `MoviePath` は生パスのまま保持する。
- `Tools.GetHashCRC32` の呼び出し仕様（`noHash` でスキップ）を維持する。

## 4. 実装フェーズ

## Phase 0（事前スパイク）
- [ ] FFMediaToolkit 採用バージョンを固定し、対応FFmpeg DLL要件を確定する。
- [ ] `tools/ffmpeg` とは別に、FFMediaToolkit用 shared DLL 配置先を決める（例: `tools/ffmpeg-shared`）。
- [ ] 起動時にDLLロード可否を検証する小さな初期化コードを作る（失敗理由をログ化）。

成果物:
- DLL構成表（必要ファイル名）
- ロード可否ログ（成功/失敗）

## Phase 1（最小差分導入）
- [ ] `IndigoMovieManager_fork.csproj` に FFMediaToolkit 依存を追加する。
- [ ] FFmpeg shared DLL を出力へコピーする設定を追加する。
- [ ] `Models/MovieInfo.cs` を FFMediaToolkit 読み取りへ置換する。
- [ ] 読み取り失敗時のみ OpenCvSharp へフォールバックする。

実装方針:
- まず FFMediaToolkit で `FPS / TotalFrames / Duration` を取得。
- `TotalFrames` が無効なら `DurationSec * FPS` を採用。
- 例外時は OpenCV経路へ戻し、登録停止を避ける。

## Phase 2（初期化と運用ログ）
- [ ] FFmpeg shared DLL ローダーを追加し、アプリ起動時に1回だけ初期化する。
- [ ] 初期化失敗時は「FFMediaToolkit無効 + OpenCVフォールバック」に固定する。
- [ ] `Watcher/MainWindow.Watcher.cs` の既存計測ログに、`MovieInfo` 取得時間の比較ログを追加する。

## Phase 3（回帰テスト）
- [ ] ASCIIパス動画で `MovieLength / FPS / TotalFrames` が従来同等であることを確認。
- [ ] 絵文字パス動画で `new MovieInfo(...)` が失敗しないことを確認。
- [ ] 監視追加スキャン（大量投入）で登録件数欠落がないことを確認。
- [ ] `noHash=true` 経路で性能退行がないことを確認。

## 5. 受け入れ条件
- 機能:
  - ASCII/絵文字の両パスで `MovieInfo` 生成が成功する。
  - `MovieLength` が0または異常値にならない。
  - 既存DBへの登録項目（`movie_length`, `movie_size`, `hash`）に退行がない。
- 性能:
  - `CheckFolderAsync` の `movieinfo_ms` が現状比で悪化しない（同等以上）。
- 運用:
  - FFMediaToolkit初期化失敗時も登録フローが止まらず、OpenCV経路で継続する。

## 6. リスクと対策
- DLL不整合で初期化失敗:
  - 対策: 起動時ロード + 明示ログ + OpenCVフォールバック固定。
- 特定動画で `NumberOfFrames` 取得不可:
  - 対策: `Duration * FPS` 推定、最終的に `0` 許容。
- 導入後に予期せぬ性能低下:
  - 対策: `Watcher` 既存計測ログで導入前後を比較し、閾値超過時は切戻し。

## 7. ロールバック方針
- 切替は `MovieInfo` に限定し、他経路（サムネイル生成）は触らない。
- FFMediaToolkit経路を無効化するフラグ（または初期化失敗判定）で即時OpenCVに戻せるようにする。
- 依存追加コミットと `MovieInfo` 切替コミットを分離し、段階的に戻せる状態を維持する。

## 8. 実行順（推奨）
1. Phase 0（DLL要件確定）
2. Phase 1（`MovieInfo` 切替 + フォールバック）
3. Phase 2（初期化とログ）
4. Phase 3（回帰確認）
