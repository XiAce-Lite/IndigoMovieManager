# Implementation Plan + tasklist（サムネイル進捗タブ ミニスレッドパネル表示レスポンス追加改善 2026-03-05）

## 0. 背景
- 2026-03-04 時点の WB-001〜WB-013 で、メモリプレビュー直結と fallback 軽量化は実装済み。
- ただし進捗UI更新が 500ms タイマー主導のため、表示反映が「最短でも次Tick待ち」になりやすい。
- `UpdateThumbnailProgressUi` は UI スレッドで `CreateSnapshot` + `Apply` + メーター読み取りをまとめて行っており、負荷時にミニパネルの追従が鈍る余地がある。

## 1. 目的
- ミニスレッドパネルの「保存完了 -> 画像反映」体感遅延をさらに短縮する。
- 高負荷時でも、進捗タブ表示が一覧操作の足を引っ張らない状態へ寄せる。
- 既存の UI 非依存境界（エンジン/Queue プロジェクト）を崩さない。

## 2. 追加の受け入れ基準（今回）
- 画像反映遅延（保存完了 -> ミニパネル表示）
  - P95 <= 400ms
  - Max <= 1200ms（I/O異常時を除く）
- `UpdateThumbnailProgressUi` 相当のUI反映処理時間
  - P95 <= 8ms
  - Max <= 20ms
- 100件連続投入中でも、一覧スクロール/選択の体感詰まりが目立たない。
- スレッドパネルは `1..最大並列数` の番号順で固定表示し、実行中に削除・移動しない。
- 完了パネル更新仕様
  - ジョブ投入時: 対応スロットの動画ファイル名を進捗UI更新タイマーに縛られず即時更新する（画像は前回表示を維持可）。
  - ジョブ完了時: 対応スロットの画像を更新する。
  - 同一パネルに連続して同一動画（同一 `MoviePathKey + TabIndex`）が来た場合、ファイル名・画像の再代入を行わない。

## 2.1 完了パネル表示仕様（明文化）
- 基本イベント
  - `onJobStarted`: ファイル名のみ更新対象。進捗UI更新タイマーに縛られず即時更新する。
  - `onJobCompleted` / `MarkThumbnailSaved`: 画像更新対象。
- 同一動画判定
  - 判定キーは `MoviePathKey + TabIndex`。
  - 判定キーが同一かつ表示中データが同値なら、UI通知をスキップする。
- 目的
  - 完了済みパネルの無駄なチラつき防止。
  - 同一動画の連続処理時にUI更新負荷を増やさない。

## 3. スコープ
- IN
  - 進捗UI更新のイベント駆動化（画像更新とメーター更新を分離）
  - ViewState 反映の差分最適化（全量反映の削減）
  - 計測ログ強化（UI適用時間・反映遅延の継続監視）
- OUT
  - サムネイル生成エンジンのアルゴリズム変更
  - QueueDBスキーマ変更 / 拡張子運用変更（`.queue.imm`）
  - サムネイル保存フォーマット変更

## 4. 方針
- 画像更新はイベント駆動、メーター類は低頻度タイマーへ分離する。
- `Apply` を「毎回フル同期」から「dirty 項目のみ反映」に寄せる。
- 改善効果は体感ではなく CSV 指標（P50/P95/Max）で判定する。

## 5. フェーズ

### Phase A: 更新ループ分離（最優先）
- `UpdateThumbnailProgressUi` を2系統へ分離する。
  - 画像/ワーカーパネル更新: イベント駆動（`MarkThumbnailSaved` / `MarkJobStarted` / `MarkJobCompleted` 契機）
  - CPU/GPU/HDDメーター更新: 既存タイマー（500ms 〜 1000ms）で継続
- 画像更新要求は `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)` でキューし、短時間でバーストが来ても coalesce する。
- 完了条件
  - 保存直後の表示反映がタイマーTick待ちに依存しない。

### Phase B: ViewState差分反映の徹底
- `ThumbnailProgressViewState.Apply` に差分判定を追加し、未変更値への代入を避ける。
- `QueueLogs` は `Clear + Add` を廃止し、末尾追加/先頭削除のリングバッファ同期へ変更する。
- `WorkerPanels` は既存再利用を維持しつつ、`StatusText`/`MovieName`/`PreviewRevision` の変更時だけ通知させる。
- 完了条件
  - 500msごとの全量更新がなくなり、UIスレッド処理時間が低下する。

### Phase C: Snapshot生成コスト削減
- `ThumbnailProgressRuntime.CreateSnapshot` の高頻度アロケーションを抑える。
  - 変更が無い周期では前回Snapshotを再利用
  - もしくは `Version` カウンタ方式で、UI側は同Versionなら反映をスキップ
- 完了条件
  - 進捗更新の無変化周期で不要なUI更新が走らない。

### Phase D: 計測・回帰
- 既存 `thumbnail-progress-latency.csv` に加えて、UI反映時間ログを追加する（例: `thumbnail-progress-ui.csv`）。
- `source_type=memory/file` 比率、P50/P95/Max を定点記録する。
- 高負荷手動回帰（100件連続投入 + 同一動画連続再生成 + UI操作）を実施する。
- 完了条件
  - 受け入れ基準を満たし、退行がない。

## 6. タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| WBX-000 | 完了 | スレッドパネル固定スロット化（番号順・非削除・非移動） | `ModelViews/ThumbnailProgressViewState.cs` | `ConfiguredParallelism` 分の固定パネルが生成され、`Remove/Move` されない |
| WBX-001 | 完了 | 画像更新とメーター更新のループを分離 | `MainWindow.xaml.cs` | 画像更新がイベント駆動で実行される |
| WBX-002 | 完了 | 画像更新要求の coalesce（連打吸収）を実装 | `MainWindow.xaml.cs` | バースト時もUI更新が過密実行されない |
| WBX-003 | 完了 | ViewState の QueueLogs 差分同期化 | `ModelViews/ThumbnailProgressViewState.cs` | `Clear + Add` を廃止し差分反映になる |
| WBX-004 | 完了 | WorkerPanel通知の最小化を実装 | `ModelViews/ThumbnailProgressViewState.cs` | 未変更項目の通知が発生しない |
| WBX-005 | 完了 | Runtime Snapshot の Version/再利用最適化 | `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressRuntime.cs` | 変更なし周期の反映がスキップされる |
| WBX-006 | 完了 | UI反映処理時間ログを追加 | `MainWindow.xaml.cs` `logs/*` | P50/P95/Max をCSVで集計可能 |
| WBX-007 | 完了 | 単体テスト追従（差分更新/Version） | `Tests/IndigoMovieManager_fork.Tests/*` | 差分反映ロジックのテストが追加される |
| WBX-008 | 未着手 | 高負荷手動回帰を実施 | 回帰メモ | 受け入れ基準を満たす |
| WBX-009 | 未着手 | 完了パネル更新仕様を実装（投入=ファイル名、完了=画像） | `ThumbnailProgressRuntime.cs` `ThumbnailProgressViewState.cs` | 開始時にファイル名のみ、完了時に画像更新が確認できる |
| WBX-010 | 未着手 | 同一動画更新スキップを実装 | `ThumbnailProgressRuntime.cs` `ThumbnailProgressViewState.cs` | 同一 `MoviePathKey + TabIndex` で不要な再代入/通知が発生しない |

## 7. 実装順（推奨）
1. WBX-001/002（イベント駆動化）で体感遅延の主因を先に除去する。
2. WBX-003/004（差分反映）でUIスレッド負荷を下げる。
3. WBX-005（Snapshot最適化）で無駄更新を止める。
4. WBX-006/007/008（計測・検証）で数値確認して締める。

## 8. リスクと対策
- リスク: イベント駆動化で更新漏れが出る
  - 対策: 低頻度タイマーをフェイルセーフとして残す（完全撤去しない）
- リスク: 差分判定の複雑化でバグ混入
  - 対策: `QueueLogs` と `WorkerPanels` を分けて段階導入し、テストで固定する
- リスク: ログ追加によるI/O増加
  - 対策: 追記のみ・軽量CSV、必要ならサンプリング率を設定化する
