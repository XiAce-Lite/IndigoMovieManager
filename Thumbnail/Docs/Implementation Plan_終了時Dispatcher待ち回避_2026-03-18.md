# Implementation Plan_終了時Dispatcher待ち回避_2026-03-18

最終更新日: 2026-03-18

## 背景

- `queue-consumer` / `thumbnail-sync` のバックグラウンド処理が、成功直後の UI 反映で `Dispatcher.InvokeAsync(...)` 完了待ちを行っていた。
- その最中に終了処理や DB 切替が入ると、UI スレッド側が閉じ処理を進めつつ、バックグラウンド側は UI 応答待ちで止まり、Visual Studio 上で `[Deadlocked...]` フレームが出ることがあった。

## 今回の方針

1. 通常時の UI 反映は維持する。
2. ただし、入力停止中・Dispatcher 終了中・キャンセル要求中は、`CreateThumbAsync(...)` の UI 反映待ちをスキップする。
3. `rescued sync` は `Dispatcher.InvokeAsync(...)` に `CancellationToken` を通し、終了や切替では `reflected` 確定前に抜けられるようにする。

## 実装メモ

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `ShouldSkipThumbnailUiReflection(...)` を追加。
  - MovieId/Hash 補完と成功後の UI 反映を、終了・切替中は待たずに抜ける形へ変更。
- `Thumbnail/MainWindow.ThumbnailFailureSync.cs`
  - rescued 反映と最終リフレッシュに `CancellationToken` を通す。
  - 入力停止や Dispatcher 終了中は、その場で同期を抜けて後続へ持ち越す。
- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`
  - UI 反映スキップ条件のテストを追加。

## 期待する効果

- アプリ終了や DB 切替の境目で、バックグラウンド処理が UI 応答待ちのまま固まる経路を減らす。
- 通常時のサムネ生成テンポは変えず、終了・切替時だけ安全側へ倒す。
