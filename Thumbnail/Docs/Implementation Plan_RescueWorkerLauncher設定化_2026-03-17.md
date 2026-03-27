# Implementation Plan_RescueWorkerLauncher設定化_2026-03-17

## 1. 目的

- `ThumbnailRescueWorkerLauncher` を app host の薄い実行器へ寄せる
- worker の探索元、補助依存の補完元、session/log/failuredb の保存先を host 設定としてまとめる
- 将来の `Publish` 移行時に、launcher 本体ではなく host 設定だけ差し替えれば済む形にする

## 2. 今回の反映

- `ThumbnailRescueWorkerLaunchSettings`
  - `SessionRootDirectoryPath`
  - `LogDirectoryPath`
  - `FailureDbDirectoryPath`
  - `HostBaseDirectory`
  - `WorkerExecutablePath`
  - `SupplementalDirectoryPaths`
  - `SupplementalFilePaths`
  を1つの設定型へ集約
- `ThumbnailRescueWorkerLaunchSettingsFactory`
  - worker exe の探索順と補助依存一覧の解決を host 側 helper へ移動
- `ThumbnailRescueWorkerLauncher`
  - `AppLocalDataPaths` 直読を除去
  - `IMM_THUMB_RESCUE_WORKER_EXE_PATH` 直読を除去
  - worker exe の所在と補助依存補完は、host から渡された具体値を使う形へ変更
- `MainWindow.ThumbnailRescueWorkerLauncher`
  - app host が `AppLocalDataPaths` を渡し、factory 経由で `ThumbnailRescueWorkerLaunchSettings` を構築

## 3. 完了条件

- launcher が `AppLocalDataPaths` を直接参照しない
- launcher が環境変数を直接参照しない
- worker 探索元と補助依存補完元を host が明示できる
- worker の引数仕様と launcher 設定仕様が別々に文書化されている
- app build と launcher 系テストが通る

## 4. 今の意味

- 短期の `Host設定（Launcher）` 案はこの形でかなり固まった
- `Publish` へ寄せる差し替えは `Factory` 側へ閉じた
- 現在は `compatibilityVersion` 一致の marker 付き publish artifact を bin より優先し、その時だけ補助依存一覧を空で返す

## 5. 残件

- 現在の factory はまだ debug / release bin 探索フォールバックを内包している
- `Publish artifact` の生成は script 前提で、CI 自動化はまだ無い
- repo 分離前に、本体バージョンと worker artifact バージョンをどこまで厳密対応付けるかを決める
