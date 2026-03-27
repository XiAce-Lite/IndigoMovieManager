# Implementation Plan_Queue_Runtime参照除去_2026-03-17

## 1. 目的

- `IndigoMovieManager.Thumbnail.Queue` から `IndigoMovieManager.Thumbnail.Runtime` への direct 参照を外す
- `QueueDb` / `FailureDb` / 補助ログの保存先規約を Queue 内へ固定せず、host 注入へ寄せる
- 将来の別 repo 化で `Queue/FailureDb` を本体 host 基盤から切り離しやすくする

## 2. 今回の反映

- `ThumbnailQueueHostPathPolicy`
  - `QueueDb` / `FailureDb` / `logs` の保存先を host から設定する薄い static 境界を追加
  - 未設定時だけ `AppContext.BaseDirectory\\thumbnail-runtime\\...` の fallback を使う
- `QueueDbPathResolver`
  - `ThumbnailQueueHostPathPolicy.ResolveQueueDbDirectoryPath()` を使う形へ変更
- `ThumbnailFailureDbPathResolver`
  - `ThumbnailQueueHostPathPolicy.ResolveFailureDbDirectoryPath()` を使う形へ変更
- `ThumbnailQueueProcessor`
  - `thumb_decode.log` の出力先を `ThumbnailQueueHostPathPolicy.ResolveLogDirectoryPath()` へ変更
- `IndigoMovieManager.Thumbnail.Queue.csproj`
  - `Thumbnail.Runtime` の project reference を削除
- `App.xaml.cs`
  - app 起動時に `AppLocalDataPaths` から Queue host path policy を注入
- `RescueWorkerApplication`
  - `--failure-db-dir` を受けて `FailureDb` 保存先を注入
- `MainWindow.ThumbnailRescueWorkerLauncher`
  - `RescueWorkerSessionsPath / LogsPath / FailureDbPath / resolved worker executable path / supplemental dir-file list` は app host 側でまとめて launcher へ渡す形へ変更

## 3. 今の意味

- `Queue` は app 固有の `%LOCALAPPDATA%` 規約を知らない
- 本体 app は従来どおり `AppLocalDataPaths` を host 基盤として使える
- `RescueWorker` は `FailureDb` 保存先だけ親から受け取れば動ける
- launcher 自体は `AppLocalDataPaths` を直読せず、host から受けた directory を使うだけになった
- launcher 自体は `AppLocalDataPaths` と環境変数を直読せず、host から受けた設定を使うだけになった

## 4. 残件

- `QueueDb` 保存先は app host が固定しているが、外部 host 向けの設定資料はまだ薄い
- `ThumbnailQueueHostPathPolicy` は static 境界なので、将来は DI しやすい契約へ進める余地がある
- `ThumbnailRescueWorkerLauncher` の host 設定は ctor 注入なので、将来は app host 設定型として名前を明確化してもよい
