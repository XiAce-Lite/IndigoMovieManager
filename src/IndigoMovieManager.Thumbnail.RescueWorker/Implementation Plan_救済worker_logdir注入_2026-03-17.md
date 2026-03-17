# Implementation Plan_救済worker_logdir注入_2026-03-17

## 1. 目的

- `RescueWorker` が `AppLocalDataPaths` を直接参照しない形へ寄せる
- host 側で決めたログ出力先を `--log-dir` で注入し、worker 側の path policy を薄くする
- 将来の別 repo 化で、worker が本体 repo の host 基盤にべったり依存しない状態へ近づける

## 2. 今回の反映

- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
  - worker 起動引数へ `--log-dir` を追加
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - `--log-dir` を main / isolated child の両方で parse
  - `ThumbnailRescueTraceLog.ConfigureLogDirectory(...)` へ親から渡された値を使う
  - `ThumbnailCreationService` 生成時も同じ log dir を使う

## 3. この段の次で完了したこと

- `RescueWorker` 内に最小の host runtime / process log writer を持たせた
- `IndigoMovieManager.Thumbnail.Runtime` 参照は worker project から削除した
- `FailureDb` 保存先も host から `--failure-db-dir` で注入する形へ進めた

## 4. 今回やっていないこと

- `DefaultThumbnailCreationHostRuntime` / `DefaultThumbnailCreateProcessLogWriter` の差し替え責務を worker 外へ完全委譲すること

## 5. 今の意味

- worker は host 側 path policy を自分で決めなくなった
- `log-dir` は親から注入される
- `failure-db-dir` も親から注入される
- worker は最小の内部実装だけを持ち、`Runtime` 参照なしで動く
