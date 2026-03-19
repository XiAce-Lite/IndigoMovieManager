# Implementation Plan 兼タスクリスト 本exe高速スクリーナー化と救済exe完全分離 2026-03-14

最終更新日: 2026-03-20

変更概要:
- 2026-03-20 追補で、`DeleteMainFailureRecords(...)` を 200 件単位の分割削除へ更新した
  - `clear error` や明示再試行で大量対象を消す時も、SQLite のパラメータ上限に詰まりにくくした
  - delete 全体は 1 transaction でまとめ、見た目だけ消えて DB が半端に残る状態を避けた
- 2026-03-17 追補で、既に正常 jpg がある個体は rescue へ再投入しないようにした
  - main 側の enqueue 入口で既存成功 jpg を検知した時点で `pending_rescue` を積まず、stale failure 行と `#ERROR.jpg` を掃除する
  - rescue worker 側でも lease 後に既存成功 jpg を再確認し、成功済み個体は `skipped(existing_success)` で閉じる
- 2026-03-16 追補で、救済worker の route 固定方針を別紙化した
  - `Thumbnail\救済worker\Route固定方針_救済worker_2026-03-16.md`
  - `fixed / long-no-frames / ultra-short-no-frames / corrupt-or-partial` の処理順と途中昇格ルールを、2026-03-16 時点の基準として固定した
- 2026-03-15 追補で、`古い.wmv` は forced repair 経路で攻略済みになった
  - `repair_probe_negative` 後の forced repair 制御フロー修正
  - WMV/ASF の `video-only retry`
  - remux 中の non-monotonic DTS packet skip
  - repair 後の engine 順保持
  を入れ、repaired source で `ffmediatoolkit` 成功まで live で確認した
 - 2026-03-15 追補で、`out1.avi` も forced repair 経路で攻略済みになった
   - `route-ultra-short-no-frames` から `route-corrupt-or-partial` への途中昇格
   - AVI の `repair_probe_negative` 後 forced repair
   - AVI の `video-only retry`
   - missing timestamp の補完 / skip
   を入れ、repaired source の `opencv` 成功まで isolated live で確認した
- 2026-03-15 追補で、`shiroka8.mp4` も autogen の `header-fallback` 相当救済で攻略済みになった
  - `No frames decoded` 時に先頭候補を浅く再探索し、1枚取れたら既存タイル数へ複製する最小実装を autogen へ戻した
  - isolated live で `ffmpeg1pass -> ffmediatoolkit -> repair_probe_negative -> route-corrupt-or-partial -> autogen` から `rescued` を確認した
- 同じ実装で `na04.mp4` も isolated live で攻略済みになった
  - `FailureId=3567` で `ffmpeg1pass -> ffmediatoolkit -> repair_probe_negative -> route-corrupt-or-partial -> autogen` から `rescued` を確認した
- `header-fallback` 相当救済は `shiroka8.mp4` 専用ではなく、同系統の長尺 `No frames decoded` 個体へ横展開可能と判断できる
- 2026-03-15 追補で、`35967.mp4` と `みずがめ座 (2).mp4` は `route-long-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-15 追補で、`画像1枚ありページ.mkv` と `画像1枚あり顔.mkv` は `route-ultra-short-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-15 追補で、`「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4` と `2_2.mp4` は `probe_negative_fallback -> autogen` で攻略済みになった
- 2026-03-15 追補で、`真空エラー2_ghq5_temp.mp4` は `route-long-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-15 追補で、`mpcクラッシュ_NGカット.tmp.flv` は `route-ultra-short-no-frames -> autogen.direct` で攻略済みになった
- 2026-03-15 追補で、救済worker に container probe の事前判定を追加した
  - `VideoIndexRepairService.ProbeAsync(...)` を使い、`video stream not found` を文言ではなく probe 確定で `NoVideoStream` へ落とす
  - `映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv` は `ffprobe` 実測で `h264` video stream ありと確認でき、`NoVideoStream` 仮説は否定された
- 2026-03-16 追補で、`映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv` は `container_probe(video_present)` までは確認できたが、最終的には既存 `CODEC NG` 扱いへ見直した
- 2026-03-16 再整理で、同個体は `ffplay` 実測でも `unspecified pixel format` により表示できず、`NoVideoStream` ではないが既存 `CODEC NG` 扱いへ固定した
- 2026-03-16 追補で、`【ライブ配信】神回scale_2x_prob-3.mp4` は `tab-error-placeholder` 起点の `fixed / unclassified -> ffmediatoolkit.direct` で攻略済みになった
- 2026-03-16 追補で、`【ライブ配信】神回scale_2x_prob-3.mp4` は実機 `Grid` 残件としては near-black が本丸で、最終的に `route-near-black-or-old-frame -> black-retry-decimal-ffmpeg` で攻略済みになった
  - live trace で `failure_id=1508,1515,1517,1521,1523,1524` の `terminal,rescued` を確認した
  - 成功 capture 秒は `0.017 sec`
- 2026-03-16 追補で、超短尺 near-black の decimal retry は「最初に通った1枚を採用」から
  「小数秒候補を全部抜き、near-black 除外後に明るさ/彩度/コントラストで採点し、上位を panel 数ぶん並べる」へ更新した
- 2026-03-16 追補で、`みずがめ座 (2).mp4` の一部成功は `placeholder-unsupported` の偽成功だったことが分かり、救済worker では `placeholder-*` を成功扱いしないよう修正した
  - 実機上は実フレーム jpg へ戻せた
- 2026-03-16 追補で、Error タブ上部の件数は `表示行` と `実マーカー` と `救済管理` を分けて見せるようにした
- 2026-03-16 追補で、Error タブ一覧は `*.swf` を対象外として除外し、成功 jpg が既にあるタブの stale failure 行も表示しないようにした
- 2026-03-17 追補で、Error タブの `選択救済 / 一括救済` は UI スレッドで全件投入しないようにし、大量件数でもカーソルだけ動く半停止状態になりにくいようバックグラウンド投入へ切り替えた
- 2026-03-16 追補で、`_steph__094110-vid1.mp4` は `tab-error-placeholder` 起点の `fixed / unclassified -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-16 追補で、`インデックス破壊-093-2-4K.mp4` は `route-long-no-frames -> route-corrupt-or-partial -> probe_negative_fallback -> autogen` で攻略済みになった
- 2026-03-16 追補で、`mpcクラッシュ_再生できない.flv` は `route-long-no-frames -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-16 追補で、`インデックス破壊-093-2-4K.remux.mp4` は `tab-error-placeholder` 起点の `fixed / unclassified -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-16 追補で、同じ `インデックス破壊-093-2-4K.remux.mp4` で `detail-selection-error-placeholder` も `fixed / unclassified -> ffmpeg1pass.direct` で攻略済みになった
- 2026-03-16 追補で、`FailureDb.RecoverExpiredProcessingToPendingRescue(...)` を追加した
  - sync / manual enqueue / worker launch 前で stale lease を回収する
  - 実 DB `難読.09FDC56B.failure.imm` でも `failure_id=3,18` を `pending_rescue` へ回収し、`processing_rescue=0` を確認した
- 2026-03-16 追補で、回収した `failure_id=3,18` も isolated live で
  `ffmpeg1pass timeout -> route-long-no-frames -> ffmediatoolkit.direct`
  から `rescued` まで確認した
- `opencv timeout` 後の出口設計は `古い.wmv` / `out1.avi` ではクローズし、`shiroka8.mp4` も閉じた
- placeholder 束の direct 勝ち筋棚卸しは一段付いた
- 次の主戦場は、stale placeholder 束の再発防止と、まだ isolated 未確認の pending 個体整理である
 - 2026-03-15 追補で、Debug harness から main 入口を叩く時の worker 解決条件を明記した
  - `pwsh` など別ホスト経由では `AppContext.BaseDirectory` が本exeではなくホスト側へ寄る
  - この経路で worker 自動起動まで確認する時は `IMM_THUMB_RESCUE_WORKER_EXE_PATH` を与える
  - live 確認で `request_enqueued ... launch_requested=True` を取得済み
- 2026-03-15 追補で、救済改善専用の debug-only trace を追加した
  - `IMM_THUMB_RESCUE_TRACE=1` の時だけ `%LOCALAPPDATA%\\IndigoMovieManager_fork_workthree\\logs\\thumbnail-rescue-trace.csv` を出す
  - main の `request_enqueued` と worker の `plan / engine / repair / terminal` を共通列で追える
  - 記録列は `movie_path / tab_index / panel_size / route / phase / engine / action / result / reason / output_path` を基本とする
- 2026-03-15 live で、`fixed / unclassified` から `route-long-no-frames` への途中昇格を確認した
  - `rescue plan promoted: from=fixed to=route-long-no-frames` を実ログで確認した
  - `tab-error-placeholder` 起点の手動救済系でも、`ffmpeg one-pass failed` を見て long へ寄せられることを確認した
- 2026-03-15 追補で、`実動画確認サマリ_2026-03-14.ps1` の Python 実行を UTF-8 化し、`cp932` 例外を回避した
- 2026-03-15 追補で、`fixed / unclassified` からの途中昇格を追加した
  - 親 failure reason が弱い手動救済・placeholder 系でも、最初の direct failure から `route-long-no-frames` / `route-corrupt-or-partial` へ昇格できるようにした
  - prefix が噛み合わない `ultra-short` などへは途中昇格しないようにした
  - `RescueWorkerApplicationTests` に途中昇格テストを追加した
- 2026-03-15 追補で、`p4 -> p5` の優先順を固定した
  - 新 route 追加より先に、既存 route の分類語彙と repair gate を強化する方針へ明記した
  - `long-no-frames` へ timeout 系と `ffmpeg one-pass failed` 系を寄せる初手を実装した
  - `repair` は route-aware gate へ上げ、`long-no-frames` と `corrupt-or-partial` を優先して repair へ入れる形にした
- 2026-03-15 追補で、`p4` の入口として route / symptom を読む失敗束サマリへ更新した
  - `Thumbnail\救済worker\救済worker失敗束サマリ_2026-03-15.ps1` は `attempt_failed` を `RouteId / SymptomClass / Engine / FailureKind / FailureReason` で見られるようにした
  - `near-black` と `fixed fallback` の単体テストを追加した
  - 現物では `fixed / unclassified` 束がまだ多く、次段は分類語彙と route 順の調整が主対象であることを確認した
- 2026-03-15 追補で、救済exeへ症状別 route 骨格を初版実装した
  - `ClassifyRescueSymptom(...)` を追加し、`FailureKind + FailureReason + MovieSizeBytes + 拡張子` から `SymptomClass` を切るようにした
  - `BuildRescuePlan(...)` を追加し、`long-no-frames / ultra-short-no-frames / near-black-or-old-frame / corrupt-or-partial / fixed` の route ごとに engine 順と repair 有無を切り替えるようにした
  - 親行 `ExtraJson` と子行 `attempt_failed` の両方へ `RouteId / SymptomClass` を載せるようにした
  - `RescueWorkerApplicationTests` に route 分類と plan 構築の単体テストを追加した
- 2026-03-15 追補で、`attempt_failed` 束を読む補助スクリプトを追加した
  - `Thumbnail\救済worker\救済worker失敗束サマリ_2026-03-15.ps1`
  - `FailureDb` の rescue 試行失敗を engine / FailureKind / FailureReason で束ねて p2 の失敗洗い出しに使う
- 2026-03-15 追補で、救済exeの fixed 順 progress snapshot を追加した
  - 親行 `ExtraJson` に `RouteId=fixed`, `SymptomClass=unclassified`, `CurrentPhase`, `CurrentEngine`, `AttemptNo`, `SourcePath` を書く
  - `FailureDb` だけ見ても、今どの engine / phase で止まっているかを追えるようにした
  - `UpdateProcessingSnapshot(...)` の単体テストを追加した
- 2026-03-15 追補で、救済exeの中期計画を別紙化した
  - `p1` fixed 順各処理のアルゴ更新
  - `p2` 失敗洗い出し
  - `p3` 症状分岐型
  - `p4` 失敗洗い出し
  - `p5` 症状分岐型各処理のアルゴ更新
  - `p6` 失敗洗い出しから `p5` へ戻す
  を 1 本の反復計画として整理した
- 2026-03-15 追補で、救済exeの vNext 設計メモを追加した
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\*` を読み、fixed 総当たりから症状分岐型ルートへ上げる方針を整理した
  - `opencv` は最後尾維持のまま nominal timeout を engine 別に長めへ分ける
  - `FailureDb` は「親行を状態の正本、子行を失敗試行ログ」として書くアルゴを別紙へ整理した
- 2026-03-15 実動画確認で `LIVE-001` から `LIVE-003` の現時点結果を反映した
  - `LIVE-001`: 通常本線は `autogen` 1 本で失敗し、即 `pending_rescue` へ落ちることを確認した
  - `LIVE-002`: `out1.avi` は `repair_probe_negative` まで進み、`direct_exhausted` ではなくなった
  - `LIVE-003`: `ffmediatoolkit` は 49 秒で戻ったが、`opencv` が 120 秒 timeout を超えても居座る個体を確認した
  - 後続の watchdog 対象は `ffmediatoolkit` 固定ではなく、token 非対応 engine 全般、現状は `opencv` を主対象に見直す
- 2026-03-15 追補で、`opencv` hard timeout を rescue worker 子プロセス隔離で締める実装を追加した
  - `ShouldUseIsolatedChildProcess("opencv")` で `opencv` だけを別プロセス試行へ逃がす
  - timeout 超過時は親 worker が child process を kill して次の判断へ戻る
  - 引数組立/復元と失敗要約の単体テストを追加した
- 2026-03-15 追補で、`repair_probe_negative` fallback の明示 engine 順を route 昇格後も維持するよう修正した
  - `古い.wmv` の parent worker live で `route-long-no-frames -> route-corrupt-or-partial -> autogen -> opencv(timeout)` まで確認した
  - `opencv` hard timeout は live でも `HangSuspected` として落ち、親 worker が `probe_negative_fallback_exhausted` で正常終了することを確認した
- 2026-03-15 追補で、計画書の続きとして live 確認と条件付き後続タスクを追加した
  - 通常本線 `autogen` 単発化後の実動画確認項目を追加した
  - `out1.avi` の repair 到達確認と `ffmediatoolkit` 再観測を次アクションへ切り出した
  - `ffmediatoolkit watchdog` は再現時だけ着手する条件付きタスクとして整理した
- 2026-03-15 追補で、通常本線内フォールバックを停止した
  - `BuildThumbnailEngineOrder(...)` は通常自動レーンで `selectedEngine` だけを返す
  - 強制engine / 明示ヒント / 手動系だけは従来どおり複数候補を許容する
  - `AutogenExecutionFlowTests` と `AutogenRegressionTests` を「autogen 失敗時はその場で失敗」前提へ更新した
- 2026-03-15 追補で `repair gate` の初手を反映した
  - `ShouldTryIndexRepair(...)` に `frame decode failed` を追加した
  - `No frames decoded` と合わせて `out1.avi` 系の repair 入口を単体テストで固定した
  - ただし実動画での `repair probe start` 再確認は未了のため、live 確認は継続する
- 実動画確認で見えた `processing_rescue` ノイズ対策を追記
  - rescue 試行ログ行は `processing_rescue` のまま残さず `attempt_failed` として保存する
  - `HasOpenRescueRequest` は main lane のみを見るよう絞り、試行ログで duplicate 抑止が張り付かないようにする
  - 旧DBの `Lane='rescue' AND Status='processing_rescue'` は初期化時に `attempt_failed` へ移行する
  - 実動画確認サマリも main status と rescue status を分けて表示する
- 2026-03-15 00:13 の実測で `難読.wb` の rescue status が `processing_rescue=24` から `attempt_failed=24` へ移行したことを確認した
- 2026-03-15 00:20 の追補で、救済 worker に `engine / repair probe / repair` の明示 timeout を追加した
  - `CreateThumbAsync(..., CancellationToken.None)` をやめ、worker から timeout 付き token を渡す
  - `lease heartbeat` を worker stdout へ出し、`processing_rescue` 停滞時も生死を読めるようにした
  - worker timeout helper の単体テストを追加した
- 2026-03-15 00:36 の追補で、`FfmpegOnePassThumbnailGenerationEngine.RunProcessAsync()` の stderr 直列待ちを修正した
  - `ReadToEndAsync()` を先に待っていて cancellation が効かない不具合を解消した
  - `WaitForExitAsync(cts)` と stdout / stderr 読取を並行化し、3秒未満でキャンセルに抜ける再現テストを追加した
- 実動画確認で見えた `rescued` 反映飢餓対策を追記
  - `startup / queue-drained` だけでは常時投入DBで `rescued` が滞留するため、5秒間隔の軽量 periodic sync を追加する
  - worker 起動条件は変えず、`rescued -> reflected` の取り込みだけを UI timer から低頻度で回す
  - `trigger=periodic-ui-tick` の `thumbnail-sync` ログを観測点に追加する
  - 2026-03-15 00:02 の実動画確認で `難読.wb` 上の `rescued` 親行が `trigger=periodic-ui-tick reflected=1 requeued=0` で `reflected` へ進むことを確認した
- 実動画確認で見えた `rescued output missing during sync` の対策を追記
  - 外部救済 worker へ `--thumb-folder` を追加し、本exeで解決した絶対出力先を渡す
  - session コピー配下へ出力が逸れて worker 終了時に消える問題を止める
  - launcher の thumb root 解決テストと引数組み立てテストを追加する
  - 実動画でも `failure_id=14,15,16,17` で `rescued -> reflected` を確認し、各回 `requeued=0` を確認した
- 実動画確認起動ガードを追記
  - `Thumbnail\実動画確認起動ガード_2026-03-14.ps1` を追加
  - 別 repo の IndigoMovieManager 系プロセスが起動中なら、workthree の重ね起動を止める
- 実動画確認補助スクリプトを追記
  - `Thumbnail\実動画確認サマリ_2026-03-14.ps1` を追加
  - 起動中プロセス、主要ログ marker、FailureDb status 件数を 1 回で確認できるようにした
- Phase 5 の観測性補強を追記
  - 外部救済 worker の stdout / stderr を `thumbnail-rescue-worker` へ転送する
  - worker 側で `engine attempt` / `repair` / `rescue result` の console ログを追加する
  - `IMM_THUMB_RESCUE_TRACE=1` 時だけ、改善処理専用の CSV を別ファイルで出す
  - 実動画確認チェックリストとエンジン再確認手順を現実装へ更新した
- Phase 5 のテスト補強を追記
  - `TEST-002` として slow timeout failure append の往復確認を追加した
  - `TEST-004` として rescued 反映 helper のガードと詳細タブ反映確認を追加した
  - `FailureDb` 単体テスト群も現行状態へ合わせて完了扱いへ整理した
- Phase 5 の in-proc rescue 掃除を追記
  - 明示救済要求は `FailureDb` へ `pending_rescue` として記録する方式へ寄せた
  - `MainWindow.ThumbnailRescueLane.cs` の in-proc queue / worker / repair 実装を削除した
  - 明示救済の実処理も外部救済 worker に統一した
  - `HasOpenRescueRequest` を追加し、同一動画・同一タブの未完了重複投入を抑止した
- Phase 4 の並列制御 `Recovery` 残骸削除を追記
  - `ThumbnailParallelController` の `HasRecoveryDemand` / `RecoveryBacklogScore` を削除
  - 高負荷判定設定から `ThumbnailParallelHighLoadWeightRecoveryBacklog` を削除
  - 既定の高負荷感度は維持するため `SlowBacklog` 既定重みへ吸収した
  - 並列制御ログも `slow_score` までへ整理
- Phase 4 の `Recovery` 表示系削除を追記
  - `ThumbnailExecutionLane.Recovery` を削除
  - 進捗表示は `Thread n / BigMovie` の 2 系統へ整理
  - IPC DTO と internal telemetry から `RecoveryLaneBacklogCount` を削除
- Phase 4 の `IsRescueRequest` 削除を追記
  - `QueueObj.IsRescueRequest` を廃止
  - 明示救済の先頭 engine 指定は `ThumbnailJobContext.InitialEngineHint` へ置換
  - 明示救済の timeout 無効化は呼び出し引数へ置換
- Phase 3 残件の lane 縮退を追記
  - 通常レーン timeout / failure handoff を削除
  - `Recovery` への新規分類を止め、lane 判定はサイズベースへ戻す
- Phase 3 前半の `rescued` 反映を追記
  - 本exeが `rescued` 行を queue-drained / startup で取り込む
  - `rescued` 反映後は `reflected` へ倒し、重複反映を防ぐ
  - 反映時に出力が消えていた場合は `pending_rescue` へ戻す
  - 通常生成と rescued sync で UI path 反映 helper を共通化する
- Phase 2 後半レビュー反映を追記
  - `ThumbnailRescueWorkerLauncher` で `FailureDbService` を mainDb 単位にキャッシュ
  - launcher の候補解決 / generation / cleanup テストを追加
  - timeout 1 回で `FailureDb` 送りになる現仕様を注意点として明記
- Phase 2 後半の外部救済 worker 起動を追記
  - `FailureDb.HasPendingRescueWork()` を追加
  - 通常キュー drain 時に外部救済 worker を起動する接続を追加
  - 救済 worker はセッション専用フォルダへコピーして起動する
  - 7日超または最新版3世代より古いセッションを best effort で掃除する
- Phase 2 前半レビュー反映を追記
  - MainDB は「書き込み禁止、読み取りは許容」へ文言修正
  - `UpdateFailureStatus` に `processing_rescue` ガード追加
  - lease 競合テストを追加し `TEST-003` を完了へ更新
- Phase 1 実装済みの内容を反映し、古い未着手前提の記述を整理
- Phase 1 レビュー反映済み事項を追記
- `AttemptGroupId` / `LeaseOwner` の初期値責務を見直し、本exeでは空文字、救済exe lease 時に採番へ統一
- Phase 2 は「救済exe最小導入 + FailureDb lease 制御」から着手する形へ更新
- レビュー指摘を反映し、実装計画書を全面改稿
- `FailureDb` の流用方針、列責務、状態遷移、MainDB 更新パスを明文化
- 過渡期の `QueueDb retry` / `autogen retry` / `Recovery` レーン縮退順を固定
- 救済exeの engine 順、lease 値、DLL コピー掃除方針を具体化
- 実装順、完了条件、タスクリスト、ロールバック条件を一体化

## 0. 現在地

### 0.1 完了済み

- Phase 1 の `FailureDb` 最小土台は実装済み
- 本exeの terminal failure 時に `FailureDb` へ `pending_rescue` を append する接続は実装済み
- `MainDbPathHash` / `MoviePathKey` は `QueueDbPathResolver` と同じ規則を共有済み
- `UpdatedAtUtc` を含む最小スキーマは実装済み
- rescue 試行ログ行の status は `attempt_failed` へ正規化済み
- `ThumbnailFailureDbService` は本exe側でキャッシュ化済み
- `FailureDb` の WAL 設定は `ThumbnailFailureDbSchema` 側へ自前保持済み
- `GetFailureRecords(limit)` に件数上限を導入済み

### 0.2 まだ未完了

- 実動画での運用確認は継続中
  - ただし `session 出力ズレ修正` は実動画で確認済み
  - `failure_id=14,15,16,17` で worker 出力先が通常 thumb root になり、`thumbnail-sync reflected=1 requeued=0` を連続確認した
  - `難読.wb` でも busy queue 中に `periodic-ui-tick` で `rescued -> reflected` が進むことを確認した
  - `repair gate` は `out1.avi` の `repair_probe_negative` まで live 確認済み
  - 通常本線 `autogen` 単発化後に、失敗が即 `pending_rescue` へ落ちる live 確認も取得済み
  - `古い.wmv` 系は `repair_probe_negative` 後に forced repair へ進み、repaired source の `ffmediatoolkit` 成功で `rescued` まで到達することを live 確認済み
  - `opencv` hard timeout は child 隔離で締まり、forced repair の前段として収束することを確認済み
  - `shiroka8.mp4` も isolated live で攻略済みとなった
  - 次の未解消は、同系統の長尺 `No frames decoded` 個体への横展開である

### 0.3 Phase 2-4 現在地

- ここでいう Phase 2 は、まず `FailureDb` lease と「1 回起動で 1 本救済する exe」までを最小完了とする
- retry 縮退、in-proc rescue lane 既定OFF、DLL セッションコピーは続きの Phase 2.5 扱いで進める
- 先に完走経路を固定し、その後で本exeを痩せさせる
- Phase 2 前半レビューで求められた安全策は反映済み
  - `UpdateFailureStatus` は `processing_rescue` 状態のみ更新可能
  - lease 二重取得はテストで確認済み
- Phase 2 後半の初手も反映済み
  - `QueueDb DefaultMaxAttemptCount = 2`
  - `autogen retry = 1`
  - in-proc rescue lane 自動 promotion は既定OFF
  - 通常キュー drain 時だけ外部救済 worker を起動
  - 救済 worker はセッション専用フォルダへコピーして起動
  - 古いセッションフォルダは best effort で掃除
  - `ThumbnailRescueWorkerLauncher` の `FailureDbService` は mainDb 単位に使い回す
  - timeout 1 回で `Failed -> pending_rescue` へ落ちる現仕様は許容する
- Phase 3 前半も反映済み
  - `rescued` 行を startup / queue-drained で取り込む
  - 反映済みは `reflected` へ遷移させる
  - 出力欠損時は `pending_rescue` へ戻す
- 実動画確認で見えた starvation 対策も反映済み
  - `rescued` 取り込みは `startup / queue-drained / periodic-ui-tick` の 3 経路になった
  - 常時投入DBでも `rescued -> reflected` が queue drain 待ちで止まり続けないようにした
- Phase 3 の lane 縮退も反映済み
  - 通常レーン timeout / failure handoff は削除
  - `Recovery` 新規分類は停止
  - lane 判定は `Normal / Slow` のサイズベースへ戻した
- Phase 3 レビュー反映も追記
  - `FailureDb` の rescue/sync 対象 lane を `normal` 固定から `normal / slow` へ拡張
  - `slow` terminal failure も `pending_rescue -> processing_rescue -> rescued -> reflected` を通せるよう修正
  - `slow` lane の往復単体テストを追加
- Phase 4 前半も反映済み
  - `QueueDb DefaultMaxAttemptCount = 1`
  - `autogen retry = 0`
  - `autogen transient failure` は再試行せず、その場で失敗として返す
  - `HandleFailedItem` の terminal failure 期待値も 1 回前提へ更新
- Phase 4 の `IsRescueRequest` 削除も反映済み
  - `QueueObj.IsRescueRequest` を削除
  - 明示救済の `ffmpeg1pass` 先頭化は `ThumbnailJobContext.InitialEngineHint` へ移した
  - 明示救済の timeout 無効化は `disableNormalLaneTimeout` 引数へ移した
- Phase 4 の `Recovery` 表示系削除も反映済み
  - `ThumbnailExecutionLane` は `Normal / Slow` の 2 値へ整理した
  - `ThumbnailProgressRuntime` の表示は `Thread n / BigMovie` へ統一した
  - IPC DTO と internal telemetry から `RecoveryLaneBacklogCount` を削除した
  - `ThumbnailParallelController` の `Recovery` backlog 重みと score も削除した
  - 高負荷判定の既定感度は `SlowBacklog` 重みへ吸収して維持した
- Phase 5 の in-proc rescue 掃除も反映済み
  - 明示救済要求は `FailureDb` へ `pending_rescue` として記録する方式へ寄せた
  - in-proc rescue queue / worker / shutdown 配線を削除した
  - 明示救済も外部救済 worker へ統一した
  - `HasOpenRescueRequest` で同一動画・同一タブの未完了重複投入を抑止した
- Phase 5 のテスト補強も反映済み
  - slow timeout terminal failure が `slow` lane / `HangSuspected` で追記されることを追加確認した
  - rescued 反映 helper のガード条件と詳細タブ反映を追加確認した
- Phase 5 の観測性補強も反映済み
  - 外部救済 worker の stdout / stderr が `thumbnail-rescue-worker` へ流れる
  - 実動画確認手順は worker ログ転送前提へ更新した
- 実動画確認補助スクリプトも反映済み
  - 現在見ているプロセス / ログ / FailureDb が現行 workthree 実装かを 1 回で確認できる
- 実動画確認起動ガードも反映済み
  - 別 repo 実行中に誤って workthree を重ね起動しないようにした
- 実動画確認で見えた session 出力ズレも対策済み
  - worker は本exeが解決した絶対 `thumbFolder` を受け取る
  - `rescued` 後に session 削除で出力が消え、`pending_rescue` へ戻る問題を止める

## 1. 目的

- `workthree` 本線の最優先は、通常動画の一覧、キュー投入、サムネイル初動の体感テンポである。
- 難物動画の完遂責務を本exeへ残したままだと、通常系 hot path に timeout、retry、repair、救済分岐が積み上がりやすい。
- そのため本書では、次の分離を正式案として固定する。
  1. 本exeは速く試し、速く見切る
  2. 失敗は `FailureDb` へ append する
  3. 救済exeが 1 本ずつ最後まで完遂する

## 2. 結論

- 最終形は、本exeから retry と rescue lane を外す。
- ただし取りこぼし防止のため、次の順で進める。
  1. `FailureDb` 最小実装を `workthree` へ入れる
  2. 救済exeの lease 制御と状態遷移を完成させる
  3. 本exeの in-proc rescue lane を停止する
  4. 本exeの retry / repair / `IsRescueRequest` 依存を削る
- 先に受け皿を作り、その後で本exeを痩せさせる。逆順は採らない。

## 3. スコープ

### 3.1 今回やること

- `FailureDb` 最小導入
- 本exe失敗時の `FailureDb` append
- 救済exeの最小導入
- 本exeの rescue lane 停止計画
- 過渡期 retry 縮退
- `Recovery` レーン縮退

### 3.2 今回やらないこと

- WhiteBrowser `*.wb` のスキーマ変更
- `future` 側の大規模 worker / IPC / telemetry 丸移植
- 通常レーンへ新しい重い救済分岐追加
- 個別動画名での例外条件追加

## 4. `fork` 側 `FailureDb` の流用方針

`fork` 側の `ThumbnailFailureDebugDb*` は、そのまま丸持ちしない。

流用するもの:

1. SQLite ベースで append 中心に記録する考え方
2. MainDB パス hash と MoviePathKey の正規化思想
3. path resolver / schema / service / record DTO の分割方針
4. `ExtraJson` で比較材料を持てる設計

捨てるもの:

1. `Debug` を前提にした命名
2. `fork` 専用の重い列や重い観測基盤の同時導入
3. `workthree` 側の通常 hot path を重くする同期記録
4. 既存 UI や Failure タブ前提の接続

`workthree` 側では、新規に次の命名で入れる。

- `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbPathResolver.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbSchema.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbService.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureRecord.cs`

補足:

- `MainDbPathHash` と `MoviePathKey` の生成は、既存 `QueueDbPathResolver` と同じ正規化ルールを共有する。
- ただし実装は `FailureDb` 用 path resolver に閉じるか、共通 helper へ薄く切り出す。

## 5. 役割分担

### 5.1 本exeの役割

- QueueDB から通常ジョブを取る
- 最速寄り engine で短時間だけ試す
- 成功時のみ jpg と MainDB / UI 更新を行う
- 失敗時は `FailureDb` へ append し、`pending_rescue` を記録する

### 5.2 救済exeの役割

- `FailureDb` から救済対象を lease 取得する
- 1 本ずつ固定順で最後まで試す
- 各試行を `FailureDb` へ append する
- 成功時は jpg を保存し、`rescued` を記録する
- MainDB は直接触らず、本exeへの反映材料だけを残す

### 5.3 `FailureDb` の役割

- 失敗ログ
- 救済待ちキュー
- 救済中 lease 管理
- 成功 / 断念 / スキップの結果記録

## 6. MainDB 更新パス

MainDB 更新は、本exe側へ残す。

理由:

1. `*.wb` は既存 UI と同居する MainDB であり、別プロセス直書きは競合説明が難しい
2. `WhiteBrowser` 互換DBに対して、外部プロセス側で直接更新経路を増やしたくない
3. UI 更新と DB 更新の責務を同じ側へ寄せた方が読みやすい

方針:

- 救済exeは jpg 保存と `FailureDb.Status=rescued` 更新まで行う
- 救済exeの MainDB 読み取りは許容する
  - 例: `system.thum` の参照
- 本exeは `rescued` 行を軽量ポーリングまたは起動時再読込で拾う
- 現行 MainDB にサムネイルパス列は無いため、反映の主対象は UI と error タブ正常化である
- 本exeは必要な既存単一列更新経路だけを持ち、UI 反映責務を引き取る
- 本exe停止中に救済成功したものは、次回起動時に反映する

## 7. 本exeから削る責務一覧

最終的に本exeから外す責務:

1. 通常失敗後の rescue lane handoff
2. 通常レーン timeout 後の rescue lane handoff
3. in-proc rescue worker 常駐
4. rescue 専用 engine 順切替
5. repair probe / repair / remux
6. source override を使った再実行
7. 長い engine retry
8. `Recovery` レーン用の運用分岐
9. `IsRescueRequest` 起点の通常経路分岐

補足:

- JPEG 保存やフレーム読み出しの極小 retry は、I/O 安定化目的なら残してよい
- 外す対象は「難物完遂責務」であり、「軽い保存回復」まで全否定しない

## 8. 本exeに残す責務一覧

本exeに残す責務:

1. QueueDB から通常ジョブを受ける
2. 最速寄り engine を 1 回だけ試す
3. 短い timeout で見切る
4. 成功時の jpg 保存
5. MainDB / UI 反映
6. `FailureDb` への最小 append
7. `pending_rescue` 遷移
8. `rescued` 行の取り込み反映

## 9. 救済exeに寄せる責務一覧

救済exeへ寄せる責務:

1. 救済対象の lease 取得
2. engine 総当たり
3. repair / remux
4. source override 再実行
5. 長時間実行
6. 詳細な失敗分類
7. 詳細ログと比較材料の記録
8. DLL 分離実行フォルダの利用

## 10. `FailureDb` 最小スキーマ

初版は「1 動画 1 行」ではなく「1 試行 1 行 append」を基本とする。

最低限ほしい列:

- `FailureId`
- `MainDbPathHash`
- `MoviePath`
- `MoviePathKey`
- `TabIndex`
- `Lane`
- `AttemptGroupId`
- `AttemptNo`
- `Status`
- `LeaseOwner`
- `LeaseUntilUtc`
- `Engine`
- `FailureKind`
- `FailureReason`
- `ElapsedMs`
- `SourcePath`
- `OutputThumbPath`
- `RepairApplied`
- `ResultSignature`
- `ExtraJson`
- `CreatedAtUtc`
- `UpdatedAtUtc`

補足:

- `Lane` は本exe terminal failure で `normal` / `slow`、救済試行 append で `rescue` を使う
- `Status` は後述の状態遷移で使う
- `AttemptGroupId` は同じ動画の一連の救済束を追うために使う
- `UpdatedAtUtc` は状態遷移、lease 延長、ハートビートに必須とする

## 11. 列責務の区分

### 11.1 本exeが埋める列

- `MainDbPathHash`
- `MoviePath`
- `MoviePathKey`
- `TabIndex`
- `Lane=normal/slow`
- `AttemptGroupId=''`
- `AttemptNo=1`
- `Status=pending_rescue`
- `LeaseOwner=''`
- `Engine`
- `FailureKind`
- `FailureReason`
- `ElapsedMs`
- `SourcePath`
- `RepairApplied=false`
- `CreatedAtUtc`
- `UpdatedAtUtc`

### 11.2 救済exeが埋める列

- `Lane=rescue`
- `LeaseOwner`
- `LeaseUntilUtc`
- `AttemptNo` の加算
- `Engine`
- `FailureKind`
- `FailureReason`
- `ElapsedMs`
- `OutputThumbPath`
- `RepairApplied`
- `ResultSignature`
- `ExtraJson`
- `Status=processing_rescue/rescued/gave_up/skipped`
- `UpdatedAtUtc`

### 11.3 両方が埋める列

- `MainDbPathHash`
- `MoviePath`
- `MoviePathKey`
- `TabIndex`
- `AttemptGroupId`
- `Status`
- `UpdatedAtUtc`

## 12. 本exe failure 記録ポリシー

本exe失敗時は、次を必ず記録する。

1. その時点で試した engine
2. 生の `FailureReason`
3. 可能なら粗い `FailureKind`
4. `AttemptGroupId`
5. `pending_rescue`

`FailureKind` 方針:

- 初版は粗くてよい
- 判定できない場合は `Unknown`
- 本exeで重い分類は行わない

`AttemptGroupId` 方針:

- 本exeの通常失敗時は空文字で入れる
- 救済exeが lease を取得した時点で GUID を採番する
- 同じ動画の救済試行 append は、その `AttemptGroupId` を引き継ぐ

## 13. `FailureDb` 状態遷移

初版状態:

- `pending_rescue`
- `processing_rescue`
- `rescued`
- `reflected`
- `gave_up`
- `skipped`

### 13.1 基本遷移

1. 本exe失敗
2. `FailureDb` へ append
3. `pending_rescue`
4. 救済exeが lease 取得
5. `processing_rescue`
6. 成功なら `rescued`
7. 本exeが UI 反映後に `reflected`
8. 全手順失敗なら `gave_up`

### 13.2 例外遷移

- 対象ファイル消失: `skipped`
- lease 期限切れ: `processing_rescue` から `pending_rescue`
- 救済exe異常終了: lease 期限切れ後に `pending_rescue`
- rescued 出力欠損: `rescued` から `pending_rescue`

## 14. lease 制御

初版の具体値:

- 初期 lease: 5 分
- ハートビート: 60 秒ごと
- 期限切れ後の再取得: 即許可

原則:

- 1 worker = 1 movie
- 1 lease = 1 movie
- `UpdatedAtUtc` を heartbeat でも更新する

## 15. 救済exeの engine 順

初版の固定順:

1. `ffmpeg1pass`
2. `ffmediatoolkit`
3. `autogen`
4. `opencv` 最後尾

理由:

- 現行 rescue lane の `ffmpeg1pass` 先頭思想を引き継ぐ
- `opencv` は通常 hot path に混ぜず、最後尾の救済専用へ閉じる
- この順は実動画検証で見直し可能だが、初版は固定して比較可能性を優先する

## 16. 過渡期 retry / lane 縮退方針

### 16.1 Phase 1

- `QueueDb DefaultMaxAttemptCount = 5` を維持
- `autogen retry = 4` を維持
- `FailureDb` は terminal failure 記録を先に入れる
- 行動変化は最小にし、観測追加を優先する

### 16.2 Phase 2

- 救済exe導入
- `QueueDb DefaultMaxAttemptCount = 2` へ縮退
- `autogen retry = 1` へ縮退
- in-proc rescue lane は rollback 用にコードを残すが、既定では自動起動を止める

### 16.3 Phase 3

- in-proc rescue lane 停止
- timeout handoff / failure handoff 停止
- `Recovery` レーンへ新規仕事を流さない

### 16.4 Phase 4

- `QueueDb DefaultMaxAttemptCount = 1`
- `autogen retry = 0`
- `Recovery` レーン enum / telemetry / UI 文言を削る
- `IsRescueRequest` 依存を削り、明示救済は引数ベースへ寄せる

## 17. `Recovery` レーンの扱い

縮退順は次で固定する。

1. Phase 2: `Recovery` レーンは残すが、既定では新規投入しない
2. Phase 3: 進捗表示と telemetry 上では残してもよいが、運用上は休止
3. Phase 4: `Recovery` を削り、`Normal / Slow` の 2 レーンへ戻す

## 18. DLL / ネイティブ依存の扱い

- DLL ロックや更新中競合があるなら、救済exeはセッション専用フォルダへコピーして起動してよい
- コピー対象は、救済exe本体、依存 DLL、必要なら ffmpeg 系 DLL 一式に絞る
- 実行フォルダ名には version と hash を含め、混在を避ける
- 起動時に「7日超」または「最新版3世代より古い」フォルダを best effort で掃除する
- 終了後の掃除失敗は成功判定より優先しない

## 19. 実装フェーズ

### Phase 1: `FailureDb` 最小導入

狙い:

- 観測と受け皿を先に作る
- 通常経路の挙動変更は最小にする

### Phase 2: 救済exe最小導入

狙い:

- `pending_rescue` を本当に消化できる状態にする
- まずは `FailureDb` lease と 1 本救済の完走経路を先に作る
- retry 縮退と in-proc rescue lane 停止は、救済exeの最小動作確認後に順次進める

### Phase 3: in-proc rescue lane 停止

狙い:

- 本exeから重い完遂責務を外す

### Phase 4: 本exe retry / lane 削減

狙い:

- 本exe hot path を短くする
- `Recovery` と明示救済の過渡期分岐を整理する

### Phase 5: 反映と掃除

狙い:

- `rescued` 行の MainDB/UI 反映を安定化する
- 過渡期コードを消す

## 20. タスクリスト

| ID | 状態 | フェーズ | タスク | 主対象 |
|---|---|---|---|---|
| FDB-001 | 完了 | Phase 1 | `FailureDb` 用 path resolver / schema / service / record を追加する | `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\*` |
| FDB-002 | 完了 | Phase 1 | `MainDbPathHash` / `MoviePathKey` 正規化を QueueDB と揃える | `QueueDbPathResolver`, `FailureDbPathResolver` |
| FDB-003 | 完了 | Phase 1 | `UpdatedAtUtc` を含む最小スキーマを実装する | `ThumbnailFailureDbSchema.cs` |
| FDB-004 | 完了 | Phase 1 | 本exe failure 記録 DTO を作る | `ThumbnailFailureRecord.cs` |
| FDB-005 | 完了 | Phase 1 | `FailureDb` 単体テストを追加する | `Tests\IndigoMovieManager_fork.Tests\*` |
| NRM-001 | 完了 | Phase 1 | 本exe terminal failure 時に `FailureDb` append する | `Thumbnail\MainWindow.ThumbnailCreation.cs`, `src\...\ThumbnailQueueProcessor.cs` |
| NRM-002 | 完了 | Phase 1 | 本exeが埋める列の値を固定する | `ThumbnailFailureRecord.cs`, `ThumbnailCreationService.cs` |
| NRM-003 | 完了 | Phase 1 | Phase 1 は `QueueDb=5`, `autogen=4` を維持する設定を明文化する | `Thumbnail\*.md`, 必要なら設定定数 |
| RVW-001 | 完了 | Phase 1 | FailureDb の WAL 設定を QueueDb 依存から切り離す | `ThumbnailFailureDbSchema.cs` |
| RVW-002 | 完了 | Phase 1 | `ThumbnailFailureDbService` の毎回 new を避ける | `ThumbnailQueueProcessor.cs` |
| RVW-003 | 完了 | Phase 1 | `AttemptGroupId` / `LeaseOwner` の初期値を空文字へ揃える | `ThumbnailQueueProcessor.cs` |
| RVW-004 | 完了 | Phase 1 | `GetFailureRecords(limit)` を導入する | `ThumbnailFailureDbService.cs` |
| RES-001 | 完了 | Phase 2 | 救済exeプロジェクトを追加する | `src\IndigoMovieManager.Thumbnail.RescueWorker\*` |
| RES-002 | 完了 | Phase 2 | `FailureDb` から 1 件 lease 取得する処理を実装する | 救済exe, `ThumbnailFailureDbService.cs` |
| RES-003 | 完了 | Phase 2 | lease 初期 5 分 / heartbeat 60 秒を実装する | 救済exe |
| RES-004 | 完了 | Phase 2 | 救済 engine 順 `ffmpeg1pass -> ffmediatoolkit -> autogen -> opencv` を実装する | 救済exe, engine routing |
| RES-005 | 完了 | Phase 2 | 救済成功時は jpg 保存 + `FailureDb.Status=rescued` に限定する | 救済exe |
| RES-006 | 完了 | Phase 2 | DLL セッションコピー起動を実装する | `Thumbnail\ThumbnailRescueWorkerLauncher.cs`, `Thumbnail\MainWindow.ThumbnailRescueWorkerLauncher.cs` |
| RES-007 | 完了 | Phase 2 | 古いセッションフォルダ掃除を実装する | `Thumbnail\ThumbnailRescueWorkerLauncher.cs`, `AppLocalDataPaths.cs` |
| RET-001 | 完了 | Phase 2 | `QueueDb DefaultMaxAttemptCount` を `5 -> 2` に縮退する | `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs` |
| RET-002 | 完了 | Phase 2 | `autogen retry` を `4 -> 1` に縮退する | `Thumbnail\ThumbnailCreationService.cs` |
| LANE-001 | 完了 | Phase 2 | in-proc rescue lane 自動起動を既定OFFにする | `Thumbnail\MainWindow.ThumbnailRescueLane.cs`, `MainWindow.xaml.cs`, `Thumbnail\MainWindow.ThumbnailCreation.cs` |
| SYNC-001 | 完了 | Phase 3 | `rescued` 行を本exeが拾って MainDB / UI へ反映する処理を作る | `Thumbnail\MainWindow.ThumbnailFailureSync.cs`, `Thumbnail\MainWindow.ThumbnailCreation.cs` |
| SYNC-002 | 完了 | Phase 3 | 起動時に未反映 `rescued` 行を再読込する | `MainWindow.xaml.cs`, `Thumbnail\MainWindow.ThumbnailFailureSync.cs` |
| LANE-002 | 完了 | Phase 3 | timeout handoff / failure handoff を停止する | `Thumbnail\MainWindow.ThumbnailCreation.cs` |
| LANE-003 | 完了 | Phase 3 | `Recovery` レーンへ新規仕事を流さない | `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailLaneClassifier.cs`, `Thumbnail\QueueObj.cs` |
| RET-003 | 完了 | Phase 4 | `QueueDb DefaultMaxAttemptCount` を `2 -> 1` に縮退する | `ThumbnailQueueProcessor.cs` |
| RET-004 | 完了 | Phase 4 | `autogen retry` を `1 -> 0` に縮退する | `ThumbnailCreationService.cs` |
| LANE-004 | 完了 | Phase 4 | `IsRescueRequest` 依存を削る | `QueueObj.cs`, `ThumbnailEngineRouter.cs`, `ThumbnailJobContext.cs`, `MainWindow.ThumbnailCreation.cs` |
| LANE-005 | 完了 | Phase 4 | `Recovery` enum / UI文言 / telemetry を削る | `ThumbnailProgressRuntime.cs`, `ThumbnailIpcDtos.cs`, `AdminTelemetryRuntimeResolver.cs` |
| CLEAN-001 | 完了 | Phase 5 | 不要になった in-proc rescue 実装を削る | `Thumbnail\MainWindow.ThumbnailRescueLane.cs`, `MainWindow.xaml.cs`, `Thumbnail\MainWindow.ThumbnailCreation.cs` |
| TEST-001 | 完了 | 全体 | `FailureDb` 単体テスト | `Tests\IndigoMovieManager_fork.Tests\*` |
| TEST-002 | 完了 | 全体 | 本exe failure append テスト | `Tests\IndigoMovieManager_fork.Tests\*` |
| TEST-003 | 完了 | 全体 | 救済exe lease 競合テスト | `Tests\IndigoMovieManager_fork.Tests\*` |
| TEST-004 | 完了 | 全体 | `rescued` 反映テスト | `Tests\IndigoMovieManager_fork.Tests\*` |
| TEST-005 | 完了 | 全体 | retry 縮退時の回帰テスト | `Tests\IndigoMovieManager_fork.Tests\*` |
| LIVE-001 | 完了 | 後続 | 通常本線 `autogen` 単発化後に、失敗が即 `pending_rescue` へ落ちることを実動画で確認する | `Thumbnail\MainWindow.ThumbnailCreation.cs`, `Thumbnail\ThumbnailCreationService.cs`, `FailureDb` |
| LIVE-002 | 完了 | 後続 | `out1.avi` 再投入で `repair probe start` まで進むことを実動画で確認する | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, 実動画 |
| LIVE-003 | 完了 | 後続 | `古い.wmv` 系で長時間停滞の主因が何かを実動画で再観測する | `Thumbnail\Decoders\FfMediaToolkitThumbnailFrameDecoder.cs`, `Thumbnail\Engines\OpenCvThumbnailGenerationEngine.cs`, `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, 実動画 |
| RES-008 | 完了 | 条件付き | token 非対応 engine が timeout を無視した時だけ、別プロセス kill を含む watchdog を実装する | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, `Tests\IndigoMovieManager_fork.Tests\RescueWorkerApplicationTests.cs`, 実動画 |
| RES-011 | 完了 | 後続 | `opencv` の nominal timeout を engine 別に長めへ分ける | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, `Tests\IndigoMovieManager_fork.Tests\RescueWorkerApplicationTests.cs` |
| RES-011a | 完了 | 後続 | fixed 順の親行 progress snapshot を追加する | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbService.cs`, `Tests\IndigoMovieManager_fork.Tests\ThumbnailFailureDbTests.cs` |
| RES-012 | 完了 | 後続 | 救済exeへ `ClassifyRescueSymptom(...)` を追加し、症状別 route を選べるようにする | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, `Tests\IndigoMovieManager_fork.Tests\RescueWorkerApplicationTests.cs` |
| RES-013 | 完了 | 後続 | 救済exeの fixed 順を route 別 `BuildRescuePlan(...)` へ置換する | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, `Tests\IndigoMovieManager_fork.Tests\RescueWorkerApplicationTests.cs` |
| RES-014 | 完了 | 後続 | 親行 `ExtraJson` の progress snapshot 更新を追加する | `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`, `src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDbService.cs`, `Tests\IndigoMovieManager_fork.Tests\ThumbnailFailureDbTests.cs` |

## 21. 各フェーズの完了条件

### Phase 1 完了条件

- `FailureDb` が生成される
- 本exe terminal failure 時に 1 試行 1 行 append できる
- `MainDbPathHash` / `MoviePathKey` が QueueDB と同じ規則で計算される
- `UpdatedAtUtc` が正しく更新される

現状:

- すべて満たした
- レビュー指摘のうち Phase 1 で直すべき項目も反映済み

### Phase 2 完了条件

- 救済exeが `pending_rescue -> processing_rescue -> rescued/gave_up` を回せる
- lease 二重取得が起きない
- `QueueDb=2`, `autogen=1` へ縮退しても通常テンポが悪化しない

現状:

- `pending_rescue -> processing_rescue -> rescued/gave_up` の最小導線は実装済み
- lease 二重取得なしのテストを追加済み
- `QueueDb=2`, `autogen=1`, in-proc rescue lane 既定OFF まで反映済み
- 通常キュー drain 時の外部救済 worker 起動も反映済み
- セッションコピー起動と古いセッション掃除も反映済み
- session コピー起動では、worker 単体 bin に native SQLite や `tools\ffmpeg-shared\*.dll` が無い場合でも、本exe 側 `runtimes\win-x64\native\e_sqlite3.dll` と `tools\` を含む補完コピーで起動と救済経路を維持する
- timeout は 1 回で `FailureDb` 送りになるが、通常系を速く見切る方針として許容する

### Phase 3 完了条件

- 本exe内 rescue worker が既定で動かない
- handoff が止まり、失敗は `FailureDb` へ流れる
- `rescued` 行を本exeが MainDB / UI へ反映できる

現状:

- `rescued -> reflected` の同期導線は実装済み
- 起動時再読込も実装済み
- handoff 停止と `Recovery` 新規分類停止も実装済み
- Phase 3 の残件は実質クローズ、以降は Phase 4 の最終縮退へ進む

### Phase 4 完了条件

- `QueueDb=1`, `autogen=0`
- `Recovery` レーンへ新規仕事が流れない
- `IsRescueRequest` 依存が通常 hot path から消える

現状:

- `QueueDb=1`, `autogen=0` までは反映済み
- `autogen transient failure` は再試行せず、その場で失敗として `FailureDb` 側へ送る
- `IsRescueRequest` 依存削除も反映済み
- `Recovery` 表示系削除も反映済み
- Phase 4 はクローズ、残りは Phase 5 の過渡期掃除

### Phase 5 完了条件

- in-proc rescue コードを削除できる
- 過渡期フラグが不要になる
- 実装とドキュメントが一致する
- 通常本線 `autogen` 単発化後の live 確認が取れている

現状:

- 明示救済要求は `FailureDb -> 外部救済 worker` へ統一した
- in-proc rescue queue / worker / shutdown 配線は削除済み
- 計画書と運用手順も現実装へ更新済み
- `TEST-001` から `TEST-005` まで現行実装に対する単体確認は完了した
- `LIVE-001` から `LIVE-003` の実動画確認は取得済み
- `RES-008` の live 確認も取得済みで、残りは `opencv` timeout 後の出口設計である
- `ClassifyRescueSymptom(...)` と `BuildRescuePlan(...)` の初版実装も反映済み
- route は現時点で `FailureKind + FailureReason + MovieSizeBytes + 拡張子` の軽量判定だけで切っている
- `救済worker\救済worker失敗束サマリ_2026-03-15.ps1` でも route / symptom を確認できるようになった
- `attempt_failed.kind` の観測性強化として、`frame decode failed ...` は `IndexCorruption`、`ffmpeg one-pass failed` は `TransientDecodeFailure`、timeout 文言は `HangSuspected` へ寄せるようにした
- 2026-03-16 時点で、成功扱い直前に near-black jpg を reject するガードを追加した
- 2026-03-16 時点で、成功 jpg 作成時に同一動画の stale `#ERROR.jpg` を削除するようにした
- 2026-03-16 時点で、precheck と通常失敗側でも「既に正常 jpg がある個体」には `#ERROR.jpg` を再生成しないようにした
- 2026-03-16 時点で、startup / queue-drained / periodic sync 入口でも「成功 jpg と同居する stale `#ERROR.jpg`」を掃除するようにした
- 2026-03-16 時点で、救済worker 側でも near-black jpg を成功扱いにせず reject し、次の engine へ進めるようにした
- 2026-03-16 時点で、救済worker 側では near-black reject 後に同じ engine を割合ベースの別時刻で最大 4 回まで再取得できるようにした
- 2026-03-16 時点で、実行中に `QueueDb` が外部削除されて 0 byte へ戻っても、同じ `QueueDbService` インスタンスで schema を自動再生成して復旧できるようにした
- live では `C:\WhiteBrowser\難読.wb` の `failure_id=4` で、`ffmpeg1pass` 120 秒 timeout 後の `attempt_failed.kind=HangSuspected` を確認した
- live では `C:\WhiteBrowser\X.wb` の手動 worker 実行で、`failure_id=3 (shiroka8.mp4)` が
  - `ffmpeg one-pass failed -> attempt_failed.kind=TransientDecodeFailure`
  - `frame decode failed at sec=167 -> attempt_failed.kind=IndexCorruption`
  となることを確認した
- `p6` の未解決束切り分けも開始し、主課題は
  - `route-long-no-frames` の `repair_probe_negative`
  - `route-ultra-short-no-frames` の `frame decode failed at sec=14871`
  - legacy `processing_rescue` 親行残留
  に絞れた
- 現在の代表束はまだ `fixed / unclassified` が多く、`p4` は誤分類より「未分類の取りこぼし」を洗う段階である
- 次段は、新 route 追加より先に既存 route の分類語彙と repair gate を強化し、その後で route 順を洗う

## 22. ロールバック条件

次のどれかが出たら、Phase 直後の縮退を止めて一段戻す。

1. 通常動画の初動が明確に悪化する
2. `pending_rescue` が増え続けて消化できない
3. `rescued` の MainDB / UI 反映漏れが出る
4. 同一動画の二重救済が出る

## 23. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\ThumbnailCreationService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\ThumbnailEngineRouter.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\ThumbnailLaneClassifier.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbPathResolver.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.RescueWorker\Program.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\設計メモ_救済exe処理順とFailureDb書込アルゴ再考_2026-03-15.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\Route固定方針_救済worker_2026-03-16.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\黒フレーム再取得方針_2026-03-16.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\中期計画_救済exe段階改善_2026-03-15.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\救済worker失敗束サマリ_2026-03-15.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\救済worker未解決束サマリ_2026-03-15.ps1`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\救済worker\未解決束レポート_p6_2026-03-15.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\Review_本exe高速スクリーナー化と救済exe完全分離_2026-03-14.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\Review_Phase3_rescued同期_handoff削除_lane戻し_2026-03-14.md`

## 24. 直近の実施順

1. `MID-004`
   - route ごとの誤分類と失敗束を洗い、`p4` の洗い出しへ進む
2. `MID-005`
   - `p4` の結果を受けて、まず既存 route の分類語彙と repair gate を詰める
   - その後で route ごとの順序を詰める
3. `MID-006`
   - `救済worker\救済worker未解決束サマリ_2026-03-15.ps1` と `救済worker\未解決束レポート_p6_2026-03-15.md` で unresolved を route 別に切る
   - `repair_probe_negative` と stale `processing_rescue` を `p5` へ返す
4. live
   - stale placeholder 代表個体を isolated で再投入し、通常勝ち筋へ戻せるかを確認する
5. stale cleanup
   - `tab-error-placeholder` 起点の stale `processing_rescue` を最後に整理する

## 25. 完了判断

- `LIVE-001` が取れたら、「通常本線は autogen だけで早期見切り」は完了扱いにする
- `LIVE-002` が取れたら、`repair gate` の初手修正は完了扱いにする
- `LIVE-003` で token 非対応 engine が残ると分かった時点で、`RES-008` を Phase 6 相当として昇格する
- `古い.wmv` と `out1.avi` 個体については、`RES-008` と forced repair 補強で live 成功まで到達した
- 次の完了判断は、stale placeholder 代表個体の最初の勝ち筋を live で固定できるかで行う
