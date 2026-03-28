# 指示書 workthree 同時起動分離 2026-03-12

## 目的
- `IndigoMovieManager_fork` 本線と `IndigoMovieManager_fork_workthree` を同時起動しても、
  `logs / QueueDb / health snapshot / 設定` が衝突しない状態にする。

## 現状の衝突点
- `workthree` も `%LOCALAPPDATA%\IndigoMovieManager_fork` を使っている。
- そのため、少なくとも次が本線と混ざる。
  - `debug-runtime.log`
  - `QueueDb\*.queue.imm`
  - worker / coordinator の health / snapshot 系
  - 一部の設定読取

## 最小方針
- `workthree` のローカル保存先名を `IndigoMovieManager_fork_workthree` へ統一する。
- 本体の表示名や実験線の意味を残したいなら、まずは保存先名の分離を最優先にする。
- Assembly 名まで分けるのは後段でよい。まずは LocalAppData 衝突を止める。

## 必須変更
- 次の直書きを `IndigoMovieManager_fork_workthree` へ変更する。
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\App.xaml.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\DebugRuntimeLog.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbPathResolver.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Adapters\ThumbnailProgressUiMetricsLogger.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Adapters\ThumbnailPreviewLatencyTracker.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\ThumbnailCreationService.cs`

## 準必須変更
- テストと手動確認スクリプトの保存先期待値も追従する。
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Test\QueueDbPathResolverTests.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Test\TestMockServices.cs`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Test\run_queue_e2e_manual.ps1`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\scripts\export_thumbnail_log_summary.ps1`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\scripts\export_thumbnail_rescue_summary.ps1`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\scripts\find_slow_videos.ps1`

## できればやる
- 保存先名を定数 1 箇所へ寄せる。
  - 例: `AppLocalDataNames.WorkthreeRoot = "IndigoMovieManager_fork_workthree"`
- `ThumbnailLaneClassifier.cs` の `SettingsAssemblyName` は、設定読取衝突が出るなら追って分離する。
- `.csproj` の `AssemblyName / Product / Company` 分離は第2段階でよい。

## 完了条件
- `workthree` 起動中に本線を起動しても、双方の `debug-runtime.log` が混ざらない。
- `QueueDb` が別フォルダへ作られる。
- `health / control / progress snapshot` が別フォルダへ出る。
- 手動確認スクリプトが新しい保存先を参照できる。

## 今回はやらない
- MainDB 形式変更
- サムネイルアルゴリズムの移植
- worker 制御ロジックの見直し
