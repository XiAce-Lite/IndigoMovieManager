# 伝達書 救済worker Debug実行切り分け 2026-03-15

最終更新日: 2026-03-15

変更概要:
- 旧メモを、2026-03-15 夜時点の Debug 実測で再整理した
- `ffmpeg1pass` timeout 不具合の解消確認を追記した
- 未解消項目を `repair gate` と `ffmediatoolkit` 系の追加観測に絞った
- 追補として、`ShouldTryIndexRepair(...)` に `frame decode failed` を追加し、repair gate の初手を実装した
- 追補として、`out1.avi` の `repair probe` 到達確認と、`古い.wmv` の停滞主因が `opencv` 側であることを反映した
- 追補として、`opencv` の nominal timeout を 300 秒へ分離し、hard timeout 問題とは切り分ける方針を反映した
- 追補として、Debug harness や `pwsh` から main 入口を叩く場合は `IMM_THUMB_RESCUE_WORKER_EXE_PATH` を明示しないと `launch_requested=False` になる条件を追記した

## 1. 目的

- 本書は、2026-03-15 の Debug 実行で確認した救済workerの現状を、本スレ共有用に短く正確に伝えるための伝達書である。
- 旧観測のまま誤認しないよう、解消済み事項と未解消事項を分けて記す。

## 2. 対象

- 対象ログ:
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\debug-runtime.log`
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\thumbnail-create-process.csv`
- 対象 FailureDb:
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\FailureDb\難読.9A45F494.failure.imm`
- 対象実行:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\bin\x64\Debug\net8.0-windows\*`

補足:
- 旧 repo の `IndigoMovieManager_fork` 側プロセス停止後に、`workthree` の Debug 実行で再確認した。
- 本書の結論は `workthree` の現行 Debug build に対するものである。
- `IndigoMovieManager_fork_workthree.exe` を直接起動する通常 Debug 実行では、worker 探索基準は本exeの `AppContext.BaseDirectory` になる。
- 一方、`pwsh` など別ホストから `MainWindow` 入口を叩く Debug harness では `AppContext.BaseDirectory` がホスト側へ寄るため、必要に応じて `IMM_THUMB_RESCUE_WORKER_EXE_PATH` で worker 実体パスを明示する。

## 3. 結論

### 3.1 救済workerの基本導線は Debug で成立している

- `pending_rescue -> processing_rescue -> rescued -> reflected` は Debug 実行で複数本確認済みである。
- 出力jpgは `RescueWorkerSessions` ではなく通常 thumb root へ保存され、そのまま `thumbnail-sync` で反映される。

### 3.2 `ffmpeg1pass` timeout 不具合は解消済み

- 旧挙動では `ffmpeg1pass` が長時間走り続け、timeout を超えても戻らない個体があった。
- 原因は `FfmpegOnePassThumbnailGenerationEngine` で `StandardError.ReadToEndAsync()` を先に待っていたため、cancel が効かなかったことだった。
- 現在は `WaitForExitAsync(ct)` と stdout/stderr 読み取りを並行化済みで、live 実測でも timeout が効いている。

### 3.3 Debug 実測で timeout 成立を 2 パターン確認済み

- 強制 15 秒検証:
  - `プリンセスチュチュall.mkv`
  - 親行 `FailureId=274`
  - `ffmpeg1pass` は約 15 秒で timeout
  - 試行行 `FailureId=276` は `attempt_failed / HangSuspected / timeout_sec=15`
  - 続く `ffmediatoolkit` で成功し、親行 `274` は `reflected`
- 既定 120 秒検証:
  - 親行 `FailureId=25`
  - 試行行 `FailureId=282` は `attempt_failed / HangSuspected / timeout_sec=120`
  - 親行 `25` は最終的に `reflected`

要点:
- `ffmpeg1pass` timeout はコード上だけでなく、Debug の live 実行でも成立している。
- 旧メモの「長時間止まり続ける」は、少なくとも `ffmpeg1pass` については現状の主問題ではない。

### 3.4 Debug harness では worker 実体パスの明示が必要な場合がある

- `pwsh` から `MainWindow.TryEnqueueThumbnailRescueJob(...)` を直接叩いた確認では、`request_enqueued` 自体は出たが、worker 探索基準が `pwsh` 側になり `launch_requested=False` になった。
- 同じ手順で `IMM_THUMB_RESCUE_WORKER_EXE_PATH=C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.RescueWorker\bin\x64\Debug\net8.0-windows\IndigoMovieManager.Thumbnail.RescueWorker.exe` を与えると、`request_enqueued` 行で `launch_requested=True` を確認できた。
- したがって、Debug harness で main 入口だけを検証する時は、worker 実体パスの環境変数指定を標準手順として扱う。

## 4. 根拠

### 4.1 15 秒強制 timeout の根拠

- 手動で `IMM_THUMB_RESCUE_ENGINE_TIMEOUT_SEC=15` を設定して worker を起動した。
- ログ上で `engine attempt start ... timeout_sec=15` を確認した。
- 同一個体で `engine attempt exception ... timeout_sec=15` と `elapsed_ms=15053` を確認した。
- その後 `ffmediatoolkit` 成功と `rescue succeeded` を確認した。

### 4.2 既定 120 秒 timeout の根拠

- 通常の Debug 実行で、worker が `failure_id=25` を処理した。
- FailureDb 上に `attempt_failed / ffmpeg1pass / timeout_sec=120` の試行行が残った。
- その後 `thumbnail-sync` により親行 `25` は `reflected` へ進んだ。

### 4.3 成功導線の根拠

- `failure_id=14,15,16,17,18,19,20,21,22,25` は Debug 実行中に `rescued -> reflected` まで進んだことを確認した。
- `thumbnail-sync rescued sync completed` は `queue-drained` だけでなく `periodic-ui-tick` でも観測できた。

## 5. 今回のコード上の到達点

### 5.1 解消済み

- `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`
  - engine
  - repair probe
  - repair
  に明示 timeout を追加済み
- `Thumbnail\Engines\FfmpegOnePassThumbnailGenerationEngine.cs`
  - `ffmpeg1pass` の cancel 不達を解消済み

### 5.2 まだ未確定

- `FrameDecoderThumbnailGenerationEngine` / `FfMediaToolkitThumbnailFrameDecoder` のネイティブ処理は token 非対応である。
- `OpenCvThumbnailGenerationEngine` も同様に timeout 実効性を別観測で見る必要がある。
- 現在の Debug 実測で確実に潰せたのは `ffmpeg1pass` timeout 側である。

## 6. 未解消項目

### 6.1 `out1.avi` 系の repair gate は live 確認まで完了

- `frame decode failed at sec=...`
- `No frames decoded`

この系統のうち、少なくとも次は repair 候補へ上がるように修正済みである。

- `frame decode failed`
- `No frames decoded`

要点:
- 旧観測を受けた最小修正は入った。
- `failure_id=137` の Debug 実測で `repair probe start -> repair probe end detected=False -> repair_probe_negative` を確認した。
- つまり `out1.avi` はもう `direct_exhausted` では止まっていない。

### 6.2 `古い.wmv` 系の長時間停滞は、現時点では `opencv` 側が主疑い

- 旧メモの `古い.wmv` は、当時 `processing_rescue` のまま heartbeat 継続だった。
- 2026-03-15 の現行 Debug 実測では、
  - `ffmpeg1pass` は 764 ms で失敗
  - `ffmediatoolkit` は 49.2 秒で `frame decode failed at sec=97` を返した
  - その後の `opencv` 開始後に lease heartbeat だけが継続し、120 秒 timeout を超えても親行 `failure_id=139` は `processing_rescue` のままだった
- したがって、今の主疑いは `ffmediatoolkit` 固定ではなく、`opencv` を含む token 非対応 engine である。
- なお、`opencv` の nominal timeout は 300 秒へ分離済みである。
- ここでまだ残る論点は、「300 秒待つかどうか」ではなく「token 非対応時に hard timeout でどう止めるか」である。

## 7. FailureDb 実測の要点

- 親行 `25`
  - `status=reflected`
- 試行行 `282`
  - `status=attempt_failed`
  - `engine=ffmpeg1pass`
  - `reason` に `timeout_sec=120`
- 親行 `274`
  - `status=reflected`
  - 実測用に手動投入した検証行
- 試行行 `276`
  - `status=attempt_failed`
  - `engine=ffmpeg1pass`
  - `reason` に `timeout_sec=15`

補足:
- `FailureId=274` は実運用で自然発生した行ではなく、15 秒 timeout 確認のために手動投入した検証行である。

## 8. 本スレへ伝えるべき要点

1. Debug 実行で、救済workerの基本導線と `rescued -> reflected` 同期は確認済みである。
2. `ffmpeg1pass` timeout 不具合はコード修正済みで、15 秒と 120 秒の両方で live 実測できた。
3. `repair gate` の live 確認は完了した。
4. 新しい未解消は、`opencv` が 120 秒 timeout を無視して居座る個体があることだ。
5. nominal timeout を長めへ分けても、hard timeout 問題は別タスクで締める必要がある。

## 9. 推奨次アクション

### 優先 1

- `opencv` 長時間停滞の再現個体を 1 本固定し、worker 側 hard timeout の設計に入る。

### 優先 2

- 再発した `opencv` 側を中心に、rescue worker 側で別プロセス kill を含む watchdog を検討する。

### 優先 3

- FailureDb だけ見ても、親行が今どの engine で止まっているか分かる補助情報を増やす。
- `ExtraJson` か専用ログのどちらかに統一して載せると追いやすい。

## 10. 補足

- 本件でまず潰れたのは「Debug build でも timeout が効かないのでは」という疑いである。
- ここはもう主戦場ではない。
- `out1.avi` の repair gate も通るようになった。
- 次に詰めるべき主戦場は、`opencv` を含む token 非対応 engine の hard timeout である。
- 2026-03-15 16:35 の Debug live で、`C:\WhiteBrowser\難読.wb` の `failure_id=4` は `ffmpeg1pass` 120 秒 timeout 後に `engine attempt exception ... kind=HangSuspected` を出し、FailureDb にも `attempt_failed / HangSuspected` として保存された
- これにより timeout 文言の kind 寄せは live 確認済みとなった
- 2026-03-15 16:39 の手動 worker 実行で、`C:\WhiteBrowser\X.wb` の `failure_id=3 (shiroka8.mp4)` は
  - `engine attempt failed ... engine=ffmpeg1pass ... kind=TransientDecodeFailure reason='ffmpeg one-pass failed'`
  - `engine attempt failed ... engine=ffmediatoolkit ... kind=IndexCorruption reason='frame decode failed at sec=167'`
  を出し、その後 `repair probe end detected=False -> rescue gave up` へ進んだ
- これにより `ffmpeg one-pass failed` と `frame decode failed at sec=...` の kind 寄せも live 確認済みとなった
