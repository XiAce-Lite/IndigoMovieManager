# 伝達書 救済worker Debug実行切り分け 2026-03-15

最終更新日: 2026-03-15

変更概要:
 - 追補として、`古い.wmv` は `repair_probe_negative` 後の forced repair 経路で攻略済みになった
 - 決定打は `forced repair`、WMV/ASF の `video-only retry`、non-monotonic DTS packet skip、repair 後の engine 順保持である
 - repaired source では `ffmediatoolkit` が成功し、temp main DB 上で `rescued` まで確認した
 - 追補として、`repair_probe_negative` fallback の明示 engine 順保持を入れ、`古い.wmv` が `autogen -> opencv(timeout)` まで進むことを parent worker live で確認した
- 追補として、`Invoke-RescueAttemptChildLive_2026-03-15.ps1` を追加し、`古い.wmv / opencv` の direct child live で 15 秒 kill を確認した
- 旧メモを、2026-03-15 夜時点の Debug 実測で再整理した
- `ffmpeg1pass` timeout 不具合の解消確認を追記した
- 未解消項目を `repair gate` と `ffmediatoolkit` 系の追加観測に絞った
- 追補として、`ShouldTryIndexRepair(...)` に `frame decode failed` を追加し、repair gate の初手を実装した
- 追補として、`out1.avi` の `repair probe` 到達確認と、`古い.wmv` の停滞主因が `opencv` 側であることを反映した
- 追補として、`opencv` の nominal timeout を 300 秒へ分離し、hard timeout 問題とは切り分ける方針を反映した
- 追補として、Debug harness や `pwsh` から main 入口を叩く場合は `IMM_THUMB_RESCUE_WORKER_EXE_PATH` を明示しないと `launch_requested=False` になる条件を追記した
 - 追補として、`out1.avi` は `route-ultra-short-no-frames -> route-corrupt-or-partial -> forced repair -> repaired opencv` で攻略済みになった
- 決定打は AVI でも forced repair を許可したこと、AVI remux に `video-only retry` と missing timestamp 補完/skip を入れたことである
- 追補として、`shiroka8.mp4` も `autogen` の `header-fallback` 相当救済で攻略済みになった
- 決定打は、autogen へ `No frames decoded` 時の先頭候補再探索とタイル複製を最小移植したことである
- 追補として、`na04.mp4` も同じ `header-fallback` 相当救済で攻略済みになった
- `shiroka8.mp4` だけの偶然ではなく、同系統の長尺 `No frames decoded` 個体に横展開できることを確認した
- 追補として、`35967.mp4` と `みずがめ座 (2).mp4` は現行 worker で `route-long-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、`画像1枚ありページ.mkv` と `画像1枚あり顔.mkv` は `route-ultra-short-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、`「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4` と `2_2.mp4` は `probe_negative_fallback -> autogen` で攻略済みになった
- 追補として、`真空エラー2_ghq5_temp.mp4` は `route-long-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、`mpcクラッシュ_NGカット.tmp.flv` は `route-ultra-short-no-frames -> autogen.direct` で攻略済みになった
- 追補として、`【ライブ配信】神回scale_2x_prob-3.mp4` は `tab-error-placeholder` 起点の `fixed / unclassified -> ffmediatoolkit.direct` で攻略済みになった
- 追補として、`_steph__094110-vid1.mp4` は `tab-error-placeholder` 起点の `fixed / unclassified -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、`インデックス破壊-093-2-4K.mp4` は `route-long-no-frames -> route-corrupt-or-partial -> probe_negative_fallback -> autogen` で攻略済みになった
- 追補として、`mpcクラッシュ_再生できない.flv` は `route-long-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、`インデックス破壊-093-2-4K.remux.mp4` は `tab-error-placeholder` 起点の `fixed / unclassified -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、同じ `インデックス破壊-093-2-4K.remux.mp4` で `detail-selection-error-placeholder` も `fixed / unclassified -> ffmpeg1pass.direct` で攻略済みになった
- 追補として、stale placeholder だった
  - `“いつかまた会えるって信じてきた俺たちの物語”…mkv`
  - `＃556「ガンダム講座」.mkv`
  も、`ffmpeg1pass` 120 秒 timeout 後に `route-long-no-frames -> ffmediatoolkit.direct` で攻略済みになった

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

### 6.1 `out1.avi` 系は forced repair + AVI remux 補強で攻略済み

- `frame decode failed at sec=...`
- `No frames decoded`

この系統について、2026-03-15 夜に次を順に入れて live で確認した。

- `route-ultra-short-no-frames` が direct failure を見て `route-corrupt-or-partial` へ途中昇格するよう修正
- AVI でも `repair_probe_negative` 後に forced repair へ入るよう修正
- AVI repair remux で `video-only retry` を許可
- `video-only retry` 中は missing `pts/dts` を補完し、両方欠けた packet は skip するよう修正

結果:
- isolated temp main DB `out1-live.wb` 上の `FailureId=4` で
  - original source: `autogen -> ffmpeg1pass -> ffmediatoolkit -> opencv`
  - `repair_probe_negative forced repair`
  - repair remux 成功
  - repaired source: `ffmpeg1pass -> ffmediatoolkit -> autogen -> opencv`
  - `opencv` 成功
  を確認した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\out1-live-isolated\thumb\160x120x1x1\out1.#5b53d680.jpg`
- 親行は `rescued` まで到達した

補足:
- stderr には `Can't write packet with unknown timestamp` が 1 行残るが、repair 自体は成功し、その後の救済も完走した
- したがって `out1.avi` は未解消ではなく、攻略済みとして扱ってよい

### 6.2 `古い.wmv` 系は forced repair 経路で攻略済み

- 旧メモの `古い.wmv` は、`repair_probe_negative` 後に `autogen -> opencv(timeout)` まで進んでも `gave_up` で終わる段階だった。
- その後の追試で、次を順に入れた。
  - `repair_probe_negative` 後に forced repair へ本当に落ちるよう、制御フローを修正
  - WMV/ASF の remux で `video-only retry` を追加
  - remux 中の non-monotonic DTS packet を skip するよう修正
  - repair 後の再試行で、明示 engine 順を route 昇格後も保持するよう修正
- 2026-03-15 夜の live では、
  - original source で `ffmpeg1pass -> ffmediatoolkit -> autogen -> opencv(timeout)`
  - forced repair 実行
  - repaired source で `ffmpeg1pass` 失敗後に `ffmediatoolkit` 成功
  - `rescue succeeded`
  まで確認した。
- したがって `古い.wmv` は「`opencv` timeout 後の出口設計」ではなく、「forced repair を正しく通し、repair 後の `ffmediatoolkit` へ繋ぐ」ことで攻略できた。
- この系統の主戦場は、もう `古い.wmv` ではなく `shiroka8.mp4` へ移っている。

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
4. `opencv` hard timeout は child 隔離で締まり、その先の forced repair 経路まで `古い.wmv` で攻略済みである。
5. `shiroka8.mp4`、`na04.mp4`、`35967.mp4`、`みずがめ座 (2).mp4` は isolated live で攻略済みになった。長尺 `No frames decoded` 束は `autogen.header-fallback` と `ffmpeg1pass.direct` の両勝ち筋を持つ。

## 9. 推奨次アクション

### 優先 1

- stale placeholder 残留を整理し、動画そのものの未解決と UI placeholder 起点の残留を分離する。
- stale placeholder は回収だけでなく、代表2本で通常勝ち筋へ戻ることまで確認済み。

### 優先 2

- `画像1枚あり*` と `「ラ・ラ・ランド」*` の勝ち筋を攻略台帳へ統合し、次の系統へ進む。

### 優先 3

- FailureDb だけ見ても、親行が今どの engine で止まっているか分かる補助情報を増やす。
- `ExtraJson` か専用ログのどちらかに統一して載せると追いやすい。

## 10. 補足

- 本件でまず潰れたのは「Debug build でも timeout が効かないのでは」という疑いである。
- ここはもう主戦場ではない。
- `out1.avi` は forced repair + AVI remux 補強で live 成功まで到達した。
- `古い.wmv` の主戦場は閉じた。
- 次に詰めるべき主戦場は、stale placeholder の再発防止と、まだ isolated 未確認の pending 個体整理である。
- 2026-03-15 16:35 の Debug live で、`C:\WhiteBrowser\難読.wb` の `failure_id=4` は `ffmpeg1pass` 120 秒 timeout 後に `engine attempt exception ... kind=HangSuspected` を出し、FailureDb にも `attempt_failed / HangSuspected` として保存された
- これにより timeout 文言の kind 寄せは live 確認済みとなった
- 2026-03-15 16:39 の手動 worker 実行で、`C:\WhiteBrowser\X.wb` の `failure_id=3 (shiroka8.mp4)` は
  - `engine attempt failed ... engine=ffmpeg1pass ... kind=TransientDecodeFailure reason='ffmpeg one-pass failed'`
  - `engine attempt failed ... engine=ffmediatoolkit ... kind=IndexCorruption reason='frame decode failed at sec=167'`
  を出し、その後 `repair probe end detected=False -> rescue gave up` へ進んだ
- これにより `ffmpeg one-pass failed` と `frame decode failed at sec=...` の kind 寄せも live 確認済みとなった
- 2026-03-15 23:22 の isolated live では、同じ `failure_id=3 (shiroka8.mp4)` が
  - `ffmpeg1pass -> TransientDecodeFailure`
  - `ffmediatoolkit -> IndexCorruption`
  - `repair_probe_negative`
  - `route-corrupt-or-partial -> autogen`
  を通って `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\shiroka8-live-isolated\thumb\160x120x1x1\shiroka8.#7b1a4914.jpg`
- これにより、`shiroka8.mp4` は「repair 出口未解消」ではなく、「autogen の header-fallback 相当救済で攻略済み」と扱ってよい
- 2026-03-15 23:29 の isolated live では、`C:\WhiteBrowser\難読.wb` 由来の `failure_id=3567 (na04.mp4, tab=2)` も
  - `ffmpeg1pass -> TransientDecodeFailure`
  - `ffmediatoolkit -> IndexCorruption`
  - `repair_probe_negative`
  - `route-corrupt-or-partial -> autogen`
  を通って `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\na04-live-isolated\thumb\160x120x1x1\na04.#2adeebb2.jpg`
- これにより `header-fallback` 相当救済は `shiroka8.mp4` 専用ではなく、少なくとも `na04.mp4` へ横展開可能と判断してよい
- 2026-03-15 23:31 の isolated live では、`failure_id=3574 (みずがめ座 (2).mp4, tab=2)` が
  - `route-long-no-frames -> ffmpeg1pass`
  の直通で `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\aquarius-live-isolated\thumb\160x120x1x1\みずがめ座 (2).#a7ce9327.jpg`
- 2026-03-15 23:34 の isolated live では、`failure_id=3570 (35967.mp4, tab=2)` も
  - `route-long-no-frames -> ffmpeg1pass`
  の直通で `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\35967-live-isolated\thumb\160x120x1x1\35967.#bba8022d.jpg`
- この 2 本により、長尺 `No frames decoded` 束は `header-fallback` と `ffmpeg1pass.direct` の両勝ち筋を持つことが live で固まった
- 2026-03-15 23:39 と 23:40 の isolated live では、`画像1枚ありページ.mkv` と `画像1枚あり顔.mkv` が
  - `route-ultra-short-no-frames -> autogen failed -> ffmpeg1pass`
  を通って `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\singlepage-live-isolated\thumb\200x150x3x1\画像1枚ありページ.#d6d68a0c.jpg`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\singleface-live-isolated\thumb\200x150x3x1\画像1枚あり顔.#330821b9.jpg`
- 2026-03-15 23:43 と 23:44 の isolated live では、`「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4` と `2_2.mp4` が
  - `route-long-no-frames -> ffmpeg1pass failed -> ffmediatoolkit failed -> repair_probe_negative -> route-corrupt-or-partial -> autogen`
  を通って `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\lalaland1-live-isolated\thumb\200x150x3x1\「ラ・ラ・ランド」は少女漫画か！？ 1_2.#758a3277.jpg`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\lalaland2-live-isolated\thumb\200x150x3x1\「ラ・ラ・ランド」は少女漫画か！？ 2_2.#ed1d9fa2.jpg`
- 2026-03-15 23:41 の isolated live では、`真空エラー2_ghq5_temp.mp4` が
  - `route-long-no-frames -> ffmpeg1pass`
  の直通で `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\vacuum-live-isolated\thumb\200x150x3x1\真空エラー2_ghq5_temp.#cc03f27c.jpg`
- 2026-03-15 23:47 の isolated live では、`mpcクラッシュ_NGカット.tmp.flv` が
  - `route-ultra-short-no-frames -> autogen`
  の直通で `rescued` まで到達した
- 出力 jpg:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\.codex_build\mpc-crash-live-isolated\thumb\200x150x3x1\mpcクラッシュ_NGカット.tmp.#70d14229.jpg`
