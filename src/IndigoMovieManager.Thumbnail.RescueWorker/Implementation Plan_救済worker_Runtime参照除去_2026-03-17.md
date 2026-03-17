# Implementation Plan_救済worker_Runtime参照除去_2026-03-17

## 1. 目的

- `RescueWorker` から `IndigoMovieManager.Thumbnail.Runtime` 参照を除去する
- worker は host 側 path policy を引数で受け、必要最小限の既定実装だけを内部に持つ
- 将来の別 repo 化で `Runtime` を同梱しなくても動ける境界へ寄せる

## 2. 今回の反映

- `RescueWorkerHostRuntime`
  - `--log-dir` を優先しつつ、placeholder 画像と process log path を解決
- `RescueWorkerProcessLogWriter`
  - worker 内部の CSV writer
  - app 側既定 writer と同じ CSV 互換を保つ
- `IndigoMovieManager.Thumbnail.RescueWorker.csproj`
  - `Thumbnail.Runtime` の project reference を削除
- `ThumbnailRescueWorkerLauncher`
  - `--failure-db-dir` を追加し、worker が `FailureDb` 保存先規約を自分で決めない形へ変更
- `MainWindow.ThumbnailRescueWorkerLauncher`
  - session root / log dir / failure db dir / resolved worker executable path / supplemental dir-file list は app host 側でまとめて launcher へ渡す形へ変更
- `RescueWorkerApplication`
  - 起動直後に `ThumbnailQueueHostPathPolicy.Configure(...)` を呼び、`FailureDb` / 補助ログの保存先を host 注入で固定

## 3. 境界

- `Engine` から使う契約
  - `IThumbnailCreationHostRuntime`
  - `IThumbnailCreateProcessLogWriter`
- `Queue` から使う契約
  - `ThumbnailQueueHostPathPolicy`
- worker 側の既定実装
  - `RescueWorkerHostRuntime`
  - `RescueWorkerProcessLogWriter`

## 4. 残件

- `RescueWorkerSessions` の generation root はまだ app 側 launcher が握っている
- process log CSV 互換は保っているが、実装は一時的に重複している
- 将来は worker host 用の共有 package に寄せるか、CLI 注入をさらに増やして薄くするかを決める
- `QueueDb` 保存先は app host 側だけが使うため、worker にはまだ注入していない
- session generation root は worker 引数ではなく app host -> launcher の構成設定として残している
- worker source 解決は publish artifact 優先へ進んだが、debug / release bin フォールバックはまだ残している
- 補助依存補完は publish artifact 選択時は不要になったが、bin フォールバック用としてまだ残している
