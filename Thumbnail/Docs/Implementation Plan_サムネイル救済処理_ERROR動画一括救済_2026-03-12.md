# Implementation Plan サムネイル救済処理 ERROR動画一括救済 2026-03-12

最終更新日: 2026-03-20

変更概要:
- 2026-03-20 時点の現実装に合わせ、上側 `救済` タブからの `一括通常再試行` は `tab=5` 制約を bypass して元タブへ戻すことを追記
- `DeleteMainFailureRecords(...)` を 200 件単位の分割削除へ更新し、大量 `ERROR` 行の掃除で SQLite パラメータ数に詰まらないようにした
- 2026-03-14 時点の現実装に合わせ、in-proc rescue レーンの記述を「FailureDb へ要求記録し外部救済 worker へ委譲」へ更新
- `IsRescueRequest` 削除に合わせて救済の先頭 engine 指定方法を更新
- 救済時の timeout 無効化を明示引数ベースへ更新
- 現コード確認に合わせて rescue レーンの実装済み範囲を更新
- `ffmpeg1pass` 先頭化、source override、index repair service 分離を追記
- 未導入項目へ「通常レーンとの worker 共用増加は未採用」を明記
- `サムネ失敗` タブ、`選択救済` / `一括救済` / `再読込`、右クリック `サムネイル救済` の実装反映
- rescue レーンを「失敗理由選別」より「固定順フル試行」寄りに更新
- rescue レーン worker 数を `1` から `2` へ拡張

## 1. 目的

- 本書は「救済レーンを新規実装する計画書」ではない。
- 本書は、`workthree` に既に入っている救済レーンを前提に、`ERROR` 動画向けの明示救済導線をどう仕上げるかを整理する現状反映版である。
- したがって主題は 2 つに絞る。
  1. 既存救済レーンが通常動画の初動を壊していないかを固める
  2. その上で `ERROR` 動画の一括救済 UI と単体救済 UI を薄く載せる

## 2. 2026-03-13 時点の最新コード確認

### 2.1 既に入っているもの

- 明示救済要求の外部 worker 委譲
  - `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - QueueDB へ混ぜず `FailureDb` へ `pending_rescue` として記録
  - 同一動画・同一タブの未完了要求は重複投入抑止
  - `requiresIdle=true` 時は通常キューが空くまで worker 起動を待つ
  - 明示実行では通常キューと並行して worker 起動を試みる
- 通常失敗時の `FailureDb` 記録と外部 worker 起動
  - `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - terminal failure を `pending_rescue` として記録
  - 通常キュー drain 後に外部 worker が 1 本だけ起動
- 手動等間隔サムネイル作成からの明示救済
  - 選択動画を rescue レーンへ直送
  - stale `ERROR` マーカーを投入前に削除
- error プレースホルダ画像起点の救済
  - `MainWindow.xaml.cs`
  - `detail-error-placeholder`
  - `tab-error-placeholder`
- repair
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - `Thumbnail/Engines/IndexRepair/VideoIndexRepairService.cs`
  - 外部救済 worker では `direct -> repair probe/remux -> repaired source retry` の固定順で進む
  - repair の入口は失敗文言より拡張子条件を優先
  - 対象拡張子だけ一時修復を実施
  - 修復後は一時ファイルを掃除
- rescue worker の固定エンジン順
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - `ffmpeg1pass -> ffmediatoolkit -> autogen -> opencv` の固定順で総当たりする
- 明示救済時の先頭 engine 指定と timeout 無効化
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - 明示救済経由では `InitialEngineHint=ffmpeg1pass` と `disableNormalLaneTimeout=true` を使える
- repaired source の差し替え
  - `Thumbnail/ThumbnailCreationService.cs`
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - 出力先と UI 更新は元動画基準のまま、入力だけ一時修復ファイルへ差し替える
- rescue の進捗表示
  - `_thumbnailProgressRuntime` 更新
  - サイズに応じて `Thread n` または `BigMovie` 表示
- `ERROR` 動画向け UI
  - `MainWindow.xaml`
  - `サムネ失敗` タブ
  - `再読込` / `選択救済` / `一括救済`
  - 右クリック `サムネイル救済`
  - `.#ERROR.jpg` 実在ベースで候補一覧を構成
- 上側 `救済` タブからの通常再試行
  - `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs`
  - `tab=5` 表示中でも `bypassTabGate=true` で元タブ (`0..4`) の通常キューへ戻せる
- 大量失敗行の掃除安定化
  - `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
  - `DeleteMainFailureRecords(...)` は 200 件単位の分割 delete + transaction で処理する
- 関連する周辺安定化
  - `MainWindow.xaml.cs`
  - `Watcher/MainWindow.Watcher.cs`
  - 通知と UI タイマーのハンドル使用量を下げる修正が入り、実動画検証時のノイズは減っている

### 2.2 まだ未導入のもの

- UI 起点の最小ログの整理
- rescue レーンと通常レーンの worker 本数を完全共用して増やす設計
- rescue 専用の短尺黒画面判定強化を `autogen` 側へ限定導入する整理

## 3. 結論

- 今の最優先は、新しい `ERROR` 一括救済 UI ではなく、導入済み救済レーンの実動画検証である。
- `ERROR` 動画向け UI は、その検証が済んだ後に「既存 rescue レーンへの入口追加」として実装するのが安全である。
- 通常レーンとの worker 完全共用は、体感テンポ悪化の説明が立つまで採らない。
- つまり軸は次で固定する。
  1. まず既存 rescue の副作用確認
  2. 次に不足 UI の追加
  3. その後に `fork` 取り込みや別 `exe` 化を再評価

## 4. 現在の役割分担

| 導線 | 目的 | 現状 |
|---|---|---|
| `Failed -> Pending` | QueueDB 上の失敗ジョブを同条件で再試行する | 既に運用可能 |
| `手動等間隔サムネイル作成` | 選択動画を rescue レーンへ 1 本ずつ流す | 既に実装済み |
| 通常レーン `10` 秒 timeout | 通常系を速く見切る | 既に実装済み |
| terminal failure 記録 | 通常失敗を `FailureDb` へ隔離する | 既に実装済み |
| queue drain 後 worker 起動 | `pending_rescue` を 1 本ずつ処理する | 既に実装済み |
| error プレースホルダ起点救済 | 表示上の error 画像個体を rescue へ送る | 既に実装済み |
| rescue 専用 `ffmpeg1pass` 先頭順 | rescue レーンだけ別順序で試す | 既に実装済み |
| rescue 時 timeout 無効化 | 通常レーン budget を救済へ持ち込まない | 既に実装済み |
| repaired source 差し替え | 一時 remux を入力へ差し替えて再試行する | 既に実装済み |
| `サムネ失敗` タブ | `.#ERROR.jpg` の実在候補を一覧表示する | 実装済み |
| `選択救済` / `一括救済` | `ERROR` 動画をユーザー明示で再救済する | 実装済み |
| `サムネイル救済` 右クリック | 現在選択動画を rescue レーンへ送る | 実装済み |

## 5. Phase 1: 実動画検証

### 5.1 目的

- `ERROR` 動画向け UI を足す前に、既存 rescue レーンが安全に動いているかを確認する。

### 5.2 最優先確認項目

1. 通常動画で `thumbnail-rescue-request` / `thumbnail-rescue-worker` が不要に割り込まないこと
2. terminal failure が `pending_rescue` へ 1 回だけ記録されること
3. queue drain 後に外部 worker が 1 本だけ起動すること
4. repair 候補では `thumbnail-rescue-worker` に repair probe / repair の転送ログが出ること
5. `手動等間隔サムネイル作成` で stale `ERROR` マーカー削除が先に走ること
6. error プレースホルダ画像起点で `FailureDb` へ隔離されること
7. 外部 worker では `ffmpeg1pass` が先頭で動くこと
8. repair 後だけ source override で再実行されること

### 5.3 完了条件

- 通常動画の初動劣化なしを説明できる
- `pending_rescue -> processing_rescue -> rescued/reflected` をログで追える
- repair 条件を拡張子ベースで説明できる
- `ERROR` マーカー削除の発火箇所を説明できる

## 6. Phase 2: 最小ログ補強

### 6.1 方針

- 追加実装は広げない。
- `ERROR` 動画向け UI を足す前に、迷いやすい箇所だけログを足す。

### 6.2 補う候補

- `ERROR` 候補列挙開始 / 終了
- 単体救済投入
- 一括救済投入
- marker 削除成功 / 失敗
- marker が無くてスキップした件数
- error プレースホルダ起点の投入理由

## 7. Phase 3: `ERROR` 動画向け UI

### 7.1 目的

- `.#ERROR.jpg` で失敗固定された動画を、ユーザー明示で rescue レーンへ送り直せるようにする。
- 主目的は、画像データを持つのに初回取りこぼしした動画を拾い直すことである。

### 7.2 UI 導線

#### A. 全失敗動画対象のタブ

- `サムネイル` 系タブ群に `サムネ失敗` タブを追加する。
- 初版の一覧列は最小限にする。
  - 動画名
  - タブ
  - `ERROR` マーカー件数
  - 更新日時
- 上部に `再読込` / `選択救済` / `一括救済` を置く。

#### B. 右クリック 1 動画救済

- `MainWindow.xaml` の `menuContext` に `サムネイル救済` を追加する。
- 通常タブでは「現在タブの選択動画」を rescue レーンへ送る。
- `サムネ失敗` タブでは「選択中の失敗行」を rescue レーンへ送る。

### 7.3 列挙方法

- 現在 DB の動画一覧を起点にする。
- 対象タブに対して `ThumbnailPathResolver.BuildErrorMarkerPath(...)` で `.#ERROR.jpg` を求める。
- `Path.Exists(errorMarkerPath)` が真のものを候補とする。

### 7.4 実行ポリシー

- UI は新しい処理系を持たない。
- 既存の `TryEnqueueThumbnailRescueJob(...)` を利用する。
- marker 削除は投入前に行う。
- 一括でも単体でも、最終的な処理は rescue レーンの逐次実行に寄せる。
- 上側 `救済` タブの `一括通常再試行` だけは、現在タブが `5` でも元タブへ戻せるよう通常キューの tab gate を明示 bypass する。

## 8. Phase 4: `fork` 取り込みと別 `exe` 化

### 8.1 将来取り込み候補

- `MainWindow.ThumbnailFailedTab.cs`
- `ThumbnailFailedRecordViewModel`
- `FailureDb` 一式
- `MainWindow.xaml` の失敗一覧 UI

### 8.2 OpenCV 救済の扱い

- `ラ・ラ・ランド系` のように、`ffmpeg1pass` では救えず、終端 `opencv` で救える動画がある。
- これは個別動画特例ではなく、「OpenCV 終端救済を残す価値がある」代表例として扱う。
- したがって今は新分岐を増やさず、次を整理対象として持つ。
  - `No frames decoded` 系の一般条件
  - `ffmpeg1pass -> opencv` の成功実測
  - `Done` 巻き戻し防止の比較

### 8.3 別 `exe` 化

- 初版はアプリ内完結が妥当である。
- 別 `exe` 化は、失敗タブと FailureDb を本格導入する段階で再評価する。

## 9. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| RESCUE-001 | 完了 | 明示救済要求を `FailureDb` へ記録する入口 | `Thumbnail/MainWindow.ThumbnailRescueLane.cs` | 明示救済要求が重複なく積まれる |
| RESCUE-002 | 完了 | 通常失敗を `pending_rescue` へ落とし queue drain 後に worker 起動 | `Thumbnail/MainWindow.ThumbnailCreation.cs`, `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs` | 通常失敗が外部 worker へ回る |
| RESCUE-003 | 完了 | 手動等間隔からの rescue 直送と marker 削除 | `Thumbnail/MainWindow.ThumbnailCreation.cs` | 選択動画を rescue へ送れる |
| RESCUE-004 | 完了 | error プレースホルダ起点救済 | `MainWindow.xaml.cs` | error 画像個体を rescue へ送れる |
| RESCUE-005 | 完了 | worker 側 repair 導入 | `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs` | probe / repair / 再実行が動く |
| RESCUE-006 | 進行中 | 実動画検証 | `Thumbnail/*.md` | 通常テンポと rescue の一本道を説明できる |
| RESCUE-007 | 完了 | `ERROR` 候補一覧タブ | `MainWindow.xaml`, `MainWindow.ThumbnailFailedTab.cs` | 候補一覧を表示できる |
| RESCUE-008 | 完了 | 選択救済 / 一括救済ボタン | `MainWindow.xaml`, `MainWindow.ThumbnailFailedTab.cs` | 表示候補を投入できる |
| RESCUE-009 | 完了 | 右クリック `サムネイル救済` | `MainWindow.xaml`, `MainWindow.MenuActions.cs` | 単体救済を起動できる |
| RESCUE-010 | 未着手 | UI 起点の最小ログ整備 | `MainWindow.xaml.cs`, `Thumbnail/*` | 列挙から投入まで追える |
| RESCUE-011 | 保留 | rescue/通常 worker 完全共用の再評価 | `Thumbnail/*`, `src/IndigoMovieManager.Thumbnail.Queue/*` | 体感テンポ悪化なしを説明できる |
| RESCUE-012 | 保留 | FailureDb / 失敗タブ取り込み | `fork` 側一式 | `ERROR` 以外も扱いたくなった時に再開 |
| RESCUE-013 | 保留 | OpenCV 救済群の一般条件整理 | `Thumbnail/*.md` | 動画名依存でない説明が作れる |

## 10. テスト観点

- 通常動画では `thumbnail-rescue-request` / `thumbnail-rescue-worker` が不要に走らない
- 通常失敗では `pending_rescue` 記録と worker 起動が走る
- repair 対象外では repair probe / repair 転送ログが出ない
- repair 対象では probe と repair の順で worker 転送ログが出る
- 手動救済では stale `ERROR` マーカーが削除される
- 外部 worker では `ffmpeg1pass` が先頭に来る
- repaired source 再実行時も保存先サムネイルと DB 更新先が元動画基準のままである
- `ERROR` 動画一覧 UI を足す時は、同一動画の重複投入が抑止される

## 11. リスクと対策

- リスク: rescue の副作用未確認のまま UI を増やす
  - 対策: UI 追加前に Phase 1 を完了する
- リスク: `ERROR` だけでは失敗理由が薄い
  - 対策: 初版は抽出条件に限定し、理由列は将来 `FailureDb` で補う
- リスク: OpenCV で救える群を動画名で分岐し始める
  - 対策: `ラ・ラ・ランド系` は調査代表例に留め、一般条件整理を先に行う

## 12. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\ThumbnailCreationService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\ThumbnailEngineRouter.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\IndexRepair\VideoIndexRepairService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.ThumbnailFailedTab.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.xaml.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.xaml`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\手動再試行運用手順.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\救済レーン実動画確認チェックリスト_2026-03-12.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\MainWindow.ThumbnailFailedTab.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDebugDbService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_workthree_ラ・ラ・ランド2_2_recovery差分_2026-03-11.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\調査結果_ラ・ラ・ランド対策まとめ_2026-03-12.md`
