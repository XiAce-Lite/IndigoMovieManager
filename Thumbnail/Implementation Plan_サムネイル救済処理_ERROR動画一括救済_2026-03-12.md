# Implementation Plan サムネイル救済処理 ERROR動画一括救済 2026-03-12

## 1. 目的

- 本処理のサムネイル作成後に `.#ERROR.jpg` で失敗固定された動画を、ユーザー主導で再救済できるようにする。
- 導線は 2 系統にする。
  - 全失敗動画対象の `サムネイル` 系タブに置く `サムネイル救済処理` ボタン
  - 動画リスト右クリックメニューの 1 動画処理 `サムネイル救済...`
- 主目的は、画像データを含んでいるのに初回処理で取りこぼした動画を拾い直し、ユーザー利益を増やすこと。
- 処理時間は問わない。通常キューより遅くてもよい。

## 2. 背景と現状

- `workthree` にはすでに明示救済用の逐次レーンがある。
  - `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
- 既存の運用手順では、次の 3 系統がすでに定義されている。
  - `Failed -> Pending` へ戻す手動再試行
  - 一覧から 1 本ずつ救済レーンへ流す手動救済
  - 通常レーン `10` 秒 timeout または通常失敗から救済レーンへ回す自動移送
- 現状でも手動等間隔サムネイル作成から、1 本ずつ救済レーンへ流す土台はある。
- ただし今は次が不足している。
  - `ERROR.jpg` 動画だけを一覧化する UI
  - 全失敗動画をまとめて救済する入口
  - 右クリックで「通常再作成」ではなく「救済レーンへ流す」明示導線
- `IndigoMovieManager_fork` 側には将来流用できる材料がある。
  - `サムネ失敗` タブ
  - `FailureDb` 基盤
  - 失敗一覧 ViewModel と DataGrid

## 3. 要件

### 3.1 必須要件

- 対象は `本処理で失敗し、.#ERROR.jpg が置かれた動画` とする。
- 一括救済は、現在開いている MainDB に紐づく失敗動画だけを対象にする。
- 単体救済は、右クリックした動画 1 件だけを対象にする。
- 既存の `Failed -> Pending` 手動再試行と役割が衝突しないようにする。
  - `Failed -> Pending` は QueueDB 再処理
  - `サムネイル救済...` は明示救済レーン直送
- どちらも既存の通常 QueueDB ではなく、既存の明示救済レーンを優先利用する。
- 実行前に stale な `ERROR` マーカーを削除する。
- 救済中に通常の自動作成や一覧操作を止めない。

### 3.2 非機能要件

- 処理時間は長くてよい。
- UI は固まらないことを優先する。
- 同一動画の重複救済投入は抑止する。
- ログだけで「候補列挙」「投入」「marker削除」「救済結果」を追えるようにする。

## 4. 推奨方針

- 初版は `ERROR.jpg` スキャンを正として組む。
- `FailureDb` は初版必須にしない。
- 理由:
  - ユーザー要件が `ERROR.jpg` 動画の救済で明確
  - `workthree` 側にはまだ失敗タブ基盤がない
  - 既存の救済レーンを活かせる
  - `fork` からの大規模取り込みを後ろに回せる

## 5. 実装イメージ

### 5.1 失敗動画の列挙方法

- 現在DBの `movie` テーブルを起点に動画一覧を取る。
- 各動画について、現在の対象タブ群に対して `ThumbnailPathResolver.BuildErrorMarkerPath(...)` で期待される `ERROR` マーカー位置を求める。
- `Path.Exists(errorMarkerPath)` が真のものを「救済候補」とみなす。
- 初版では「`ERROR.jpg` が存在すること」を失敗判定の主条件にする。

### 5.2 UI導線

#### A. 全失敗動画対象のタブ

- `サムネイル` 系の新規タブ、または既存サムネ進捗領域に隣接する `サムネ失敗` タブを追加する。
- 表示内容は初版ではシンプルにする。
  - 動画パス
  - タブ種別
  - `ERROR` マーカー有無
  - 更新日時
- 上部に `サムネイル救済処理` ボタンを置く。
- ボタン押下で表示中の失敗動画を全件、救済レーンへ逐次投入する。

#### B. 右クリック 1 動画救済

- `MainWindow.xaml` の `menuContext` に `サムネイル救済...` を追加する。
- 対象は右クリックした 1 動画。
- 実行時は確認ダイアログを出す。
- 実行後は既存の `TryEnqueueThumbnailRescueJob(...)` を呼ぶ。

## 6. バックエンド方針

### 6.1 初版で使う既存資産

- `TryEnqueueThumbnailRescueJob(...)`
- `TryDeleteThumbnailErrorMarker(...)`
- `RunThumbnailRescueLoopAsync(...)`
- `ShouldTryThumbnailIndexRepair(...)`
- `ResetFailedThumbnailJobsForCurrentDb()`
- `ShouldUseThumbnailNormalLaneTimeout(...)`
- `ShouldPromoteThumbnailFailureToRescueLane(...)`

### 6.2 追加する責務

- `ERROR.jpg` 候補列挙
- 救済対象一覧の ViewModel
- 一括救済コマンド
- 単体救済コマンド
- 実行ログの最小補強

### 6.3 実行ポリシー

- 一括救済も単体救済も、`IsRescueRequest = true` で既存救済レーンへ積む。
- ユーザー明示操作なので `requiresIdle = false` を基本にする。
- ただし一括救済は件数が多くなりうるため、投入自体は UI スレッド外で行う。
- 実処理は救済レーン側で 1 本ずつ実行する。
- `Failed -> Pending` の再試行は既存運用手順として残し、本計画の一括救済はそれを置き換えない。
- 使い分けは次で固定する。
  - Queue失敗をそのまま同条件でやり直す: `Failed -> Pending`
  - `ERROR.jpg` 動画を重い救済順路でやり直す: `サムネイル救済処理` / `サムネイル救済...`

## 7. 画面設計の最小案

### 7.1 初版UI

- `TabItem Header="サムネ失敗"` を追加
- `DataGrid` で失敗候補一覧を表示
- ボタン:
  - `再読込`
  - `サムネイル救済処理`
- オプション:
  - `現在タブのみ`
  - `全タブ`

### 7.2 初版で持たないもの

- 高度なフィルタ
- FailureDb ベースの理由列
- 自動更新の複雑なデバウンス
- 失敗原因の集計表示

## 8. データモデル案

- `ThumbnailRescueCandidateViewModel`
  - `MovieId`
  - `MoviePath`
  - `Hash`
  - `TabIndex`
  - `TabLabel`
  - `ErrorMarkerPath`
  - `ErrorMarkerExists`
  - `LastWriteTimeUtc`

初版は `ERROR.jpg` 起点なので、この程度で十分とする。

## 9. 処理フロー案

### 9.1 一括救済

1. 現在DBと対象タブ範囲を確定する。
2. 動画一覧から `ERROR.jpg` 候補を列挙する。
3. 候補一覧を DataGrid に表示する。
4. `サムネイル救済処理` 押下で確認ダイアログを出す。
5. 各候補に対して marker 削除を試みる。
6. `QueueObj` を作り、既存救済レーンへ投入する。
7. ログへ `bulk rescue queued / started / completed / failed` を残す。

### 9.2 単体救済

1. 右クリック対象動画を確定する。
2. 対象タブに対応する `ERROR.jpg` を確認する。
3. 確認ダイアログを出す。
4. marker 削除後、救済レーンへ 1 本投入する。

### 9.3 既存手動再試行との統合運用

1. Queue上の `Failed` を同条件で再実行したい場合は `ResetFailedThumbnailJobsForCurrentDb()` を使う。
2. `ERROR.jpg` 固定済み動画を重い救済順路で再実行したい場合は、本計画の明示救済導線を使う。
3. 一覧選択から 1 本ずつ静かに流したい場合は、既存の `手動等間隔サムネイル作成` も併用可能とする。
4. ドキュメント上は `手動再試行運用手順.md` の内容を本計画へ統合し、最終的には役割分担が一目で分かる形へ揃える。

## 10. 救済カテゴリ整理

### 10.1 初版で直接扱うカテゴリ

- `ERROR.jpg` が存在する失敗固定動画
- 画像データを含み、再試行で救える見込みがある動画
- 通常レーンに戻すより、救済レーンで最後まで試した方が得な動画

### 10.2 OpenCV で救えるカテゴリ

- `ffmpeg1pass` までで拾えず、終端 `opencv` で救える動画がある。
- 代表例として、`ラ・ラ・ランド系` のような `No frames decoded` 群は、最後に `opencv` で救える可能性を持つ。
- そのため本計画では、救済レーンの成功定義を `repair` や `ffmpeg1pass` に限定しない。
- `opencv` まで含めた終端救済を、将来の分類軸として残す。

### 10.3 初版での扱い

- 初版では OpenCV 専用の新 UI や別分岐は増やさない。
- ただし次は計画へ含める。
  - 候補一覧に `OpenCV救済候補` のメモ列を持てる余地を残す
  - `ラ・ラ・ランド系` のような既知系統を、将来 `FailureDb` や調査メモと接続できるようにする
  - `fork` 取り込み時は `ffmpeg1pass -> opencv` の終端 fallback 情報も比較対象にする

## 11. ログ方針

- 初版で最低限追加するログ:
  - `thumbnail-rescue-ui candidate-scan-start`
  - `thumbnail-rescue-ui candidate-scan-end count=...`
  - `thumbnail-rescue-ui bulk-enqueue-start count=...`
  - `thumbnail-rescue-ui bulk-enqueue-item movie=... tab=...`
  - `thumbnail-rescue-ui single-enqueue movie=... tab=...`
  - `thumbnail-rescue-ui skipped-no-error-marker movie=...`

既存の `thumbnail-rescue` ログと合わせて、UI起点から実処理終端まで辿れるようにする。

## 12. `IndigoMovieManager_fork` からの将来取り込み検討

### 12.1 取り込み候補

- `MainWindow.ThumbnailFailedTab.cs`
- `ThumbnailFailedRecordViewModel`
- `FailureDb` 一式
- `MainWindow.xaml` の `サムネ失敗` タブ DataGrid
- `ffmpeg1pass -> opencv` 終端救済に関する調査メモ
- `ラ・ラ・ランド系` など OpenCV 救済実績の一般条件整理

### 12.2 取り込みタイミング

- 初版が `ERROR.jpg` スキャンで成立した後
- 失敗理由や recovery route を UI で見たくなった段階
- 一括救済対象を `ERROR.jpg` だけでなく「最終失敗全般」へ広げたくなった段階
- OpenCV で救える群を、動画名依存ではなく一般条件で残したくなった段階

### 12.3 取り込み方針

- 丸ごと取り込みではなく、次の順で薄く入れる。
  1. 失敗タブ UI
  2. FailureDb 読み取り
  3. 失敗理由列とフィルタ
  4. 必要なら救済判定の一般条件列
  5. OpenCV 終端救済の分類列

## 13. 別 `exe` 化の選択肢

### 13.1 案A: 初版はアプリ内完結

- 長所:
  - 既存救済レーンをそのまま使える
  - 実装差分が小さい
  - UIから状態が見やすい
- 短所:
  - 失敗列挙と一括投入の責務が `MainWindow` に増える

### 13.2 案B: 将来 `ThumbnailRescueTool.exe` を分離

- 役割:
  - MainDB を受け取る
  - `ERROR.jpg` を走査する
  - 候補一覧を表示する
  - 救済要求を IPC かファイル経由で本体へ渡す、または自前で処理する
- 長所:
  - UI本体の責務を増やしにくい
  - 長時間救済を別プロセスへ逃がせる
- 短所:
  - MainDB 共有、ログ共有、設定共有の設計が要る
  - 初版としては重い

### 13.3 推奨

- まずはアプリ内完結で作る。
- 別 `exe` 化は、失敗タブや FailureDb を本格導入する次段で再評価する。

## 14. 実装フェーズ案

### Phase 1: `ERROR.jpg` 候補列挙の土台

- 候補列挙サービスを追加する
- `MovieRecords` から候補 ViewModel を作る
- 単体テストで `ERROR.jpg` 検出を固定する

### Phase 2: 単体救済導線

- 右クリック `サムネイル救済...` を追加
- 確認ダイアログ追加
- 既存救済レーンへ接続

### Phase 3: 一括救済タブ

- `サムネ失敗` タブを追加
- 候補一覧 DataGrid を追加
- `サムネイル救済処理` ボタンを追加

### Phase 4: ログと運用補強

- 一括救済の件数ログ
- marker 未存在時のスキップログ
- 実行後再読込
- `Failed -> Pending` 再試行との使い分け説明を UI/文書へ反映

### Phase 5: 将来拡張

- `fork` の FailureDb / 失敗タブ取り込み
- 別 `exe` 化
- OpenCV 救済群の分類整理
- `ラ・ラ・ランド系` のような既知群の一般条件化

## 15. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| RESCUE-001 | 未着手 | `ERROR.jpg` 候補列挙ロジック追加 | `Thumbnail/*`, `ThumbnailPathResolver.cs` | 現在DBから救済候補一覧を作れる |
| RESCUE-002 | 未着手 | 救済候補 ViewModel 追加 | `ModelViews/*` または `Thumbnail/*` | DataGrid へバインドできる |
| RESCUE-003 | 未着手 | 右クリック `サムネイル救済...` 追加 | `MainWindow.xaml`, `MainWindow.MenuActions.cs` | 単体救済を起動できる |
| RESCUE-004 | 未着手 | 単体救済の確認ダイアログと実行接続 | `MainWindow.MenuActions.cs`, `Thumbnail/MainWindow.ThumbnailRescueLane.cs` | 1 動画を救済レーンへ流せる |
| RESCUE-005 | 未着手 | `サムネ失敗` タブ UI 追加 | `MainWindow.xaml` | 候補一覧とボタンが表示される |
| RESCUE-006 | 未着手 | 一括救済ボタン接続 | `MainWindow.xaml.cs` | 表示候補を全件投入できる |
| RESCUE-007 | 未着手 | 追加ログ整備 | `MainWindow.xaml.cs`, `Thumbnail/*` | UI起点ログが追える |
| RESCUE-008 | 未着手 | `Failed -> Pending` 再試行との使い分けを文書統合 | `Thumbnail/*.md` | 手動再試行と明示救済の役割が整理される |
| RESCUE-009 | 未着手 | `fork` 取り込み比較メモを別紙化 | `Thumbnail/*.md` | FailureDb移行条件を判断できる |
| RESCUE-010 | 未着手 | OpenCV 救済群の調査メモを残す | `Thumbnail/*.md` | `ラ・ラ・ランド系` などの一般条件化入口ができる |

## 16. テスト観点

- `ERROR.jpg` がある動画だけ候補に出る
- `ERROR.jpg` が無い動画は候補に出ない
- 単体救済で marker 削除後に救済レーンへ 1 回だけ入る
- 一括救済で同一動画が重複投入されない
- 救済中も通常一覧操作ができる
- repair 条件や timeout 条件は既存救済レーン側の挙動を壊さない
- `Failed -> Pending` 再試行導線と明示救済導線の役割が混ざらない
- `ffmpeg1pass` 失敗後でも OpenCV で救える動画カテゴリを後から追える

## 17. リスクと対策

- リスク: 失敗一覧の列挙が重い
  - 対策: 初版は手動再読込式にして、常時監視しない
- リスク: `ERROR.jpg` だけでは理由が分からない
  - 対策: 初版は救済対象の抽出に限定し、理由表示は将来 `FailureDb` で補う
- リスク: 大量一括投入で通常系の見え方が悪くなる
  - 対策: 明示救済レーンで逐次処理し、通常 QueueDB へ混ぜない
- リスク: `MainWindow` の責務が増える
  - 対策: 候補列挙とタブ更新は別メソッドへ切り出す前提で進める
- リスク: OpenCV で救える群を個別動画名で扱い始める
  - 対策: `ラ・ラ・ランド系` は例示に留め、一般条件化まではコード分岐にしない

## 18. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\ThumbnailPathResolver.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\手動再試行運用手順.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.xaml`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.MenuActions.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\MainWindow.ThumbnailFailedTab.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDebugDbService.cs`
