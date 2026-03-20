# Implementation Plan

## 1. 目的

- 本書は `workthree` に既に入っている救済系を、最新コード基準で検証し、仕上げるための計画書である。
- 主題は `future` から何を持ち込むかではない。
- 主題は、通常レーン、救済レーン、repair、Watcher、`ERROR` マーカー運用が噛み合っているかを固めることである。

## 2. 2026-03-13 時点の最新コード確認

- 救済レーン本体は既に入っている。
  - `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - 明示救済専用キュー
  - 重複投入抑止
  - 通常キュー active 中は `requiresIdle=true` の救済を待機
  - 救済進捗の記録と `救済Thread` 表示
- 通常レーンからの自動 handoff も既に入っている。
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - 通常レーン `10` 秒 timeout
  - 通常失敗時の rescue handoff
  - 手動等間隔サムネイル作成の rescue 直送
  - 手動時の stale `ERROR` マーカー削除
- repair も既に入っている。
  - `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - `No frames decoded` などの文言と拡張子条件に合う時だけ probe と repair を実行
  - repair 後は一時修復ファイルを掃除
- UI 上で error プレースホルダ画像が見えた時の救済投入も既に入っている。
  - `MainWindow.xaml.cs`
  - `detail-error-placeholder`
  - `tab-error-placeholder`
- 直近の関連修正として、通知とタイマーのハンドル使用量を下げる変更も入っている。
  - `MainWindow.xaml.cs`
  - `Watcher/MainWindow.Watcher.cs`
  - 実動画検証時のノイズ低減には効くが、救済ルート自体は変えない
- 一方で、次はまだ未導入である。
  - `ERROR` 動画の専用一覧タブ
  - 全失敗動画対象の一括救済ボタン
  - 右クリックの明示 `サムネイル救済...`

## 2.1 2026-03-20 時点の追加前提

- `ThumbnailCreationService` 周辺は、救済 UI や worker 側の修正を安全に進めるために先行して整理した。
- 現在の正規入口は `ThumbnailCreationServiceFactory`、`IThumbnailCreationService`、`ThumbnailCreateArgs`、`ThumbnailBookmarkArgs` である。
- `MainWindow` と `RescueWorker` の生成口は host 別 factory に分離済みで、`new ThumbnailCreationService(...)` の直呼びは本流から排除済みである。
- `create` / `bookmark` の引数検証は coordinator 側へ集約し、service 本体は delegate を受け取る facade に寄せた。
- この整理は救済レーンの速度改善そのものではないが、以後の UI / rescue / queue 変更で責務を戻さず進めるための基盤として扱う。

## 3. 結論

- 最優先は、導入済み救済レーンの実動画検証である。
- ただし、その前提となるサムネイル生成入口整理は一段落しており、以後は `Factory + Interface + Args` を崩さずに進める。
- 重点は次の 4 点に絞る。
  1. 通常動画の初動を壊していないか
  2. `10` 秒 timeout 後の handoff が意図通りか
  3. repair 発火条件が広がり過ぎていないか
  4. stale `ERROR` マーカー削除が必要箇所で効いているか
- UI テンポ改善と `ERROR` 一括救済 UI は価値があるが、救済レーンの副作用確認より後に置く。
- Queue 観測は追加実装を広げず、ログの穴だけを埋める。

## 4. 優先順位

| 優先 | 重点 | 目的 | 今回の扱い |
|---|---|---|---|
| P0 | サムネイル生成入口整理の維持 | `Factory + Interface + Args` の本流を崩さず rescue 改修を載せる | 継続前提 |
| P1 | 救済レーン実動画検証 | 通常系を壊さず rescue が流れるか確かめる | 最優先 |
| P2 | Queue 観測の最小補強 | handoff、repair、marker 制御をログで辿れるようにする | 必要最小限のみ |
| P3 | `ERROR` 動画向け明示 UI | 一括救済と単体救済の入口を足す | P1 完了後 |
| P4 | UI テンポ改善 | 再読込や一覧更新を軽くする | 次点 |
| P5 | 難読動画条件の棚卸し | OpenCV を含む一般条件整理 | 分岐追加は保留 |

## 4.1 Phase 0: サムネイル生成入口整理の維持

### 4.1.1 位置づけ

- これは rescue 新機能ではなく、以後の rescue / UI / worker 改修を薄く載せるための基盤固定である。
- `ThumbnailCreationService` を再び太らせないことを優先する。

### 4.1.2 固定前提

- 正規入口は `ThumbnailCreationServiceFactory` と `IThumbnailCreationService`
- public request は `ThumbnailCreateArgs` と `ThumbnailBookmarkArgs`
- host 別生成口は `Thumbnail/AppThumbnailCreationServiceFactory.cs` と `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerThumbnailCreationServiceFactory.cs`
- 引数検証は coordinator 側
- service 本体は delegate 受け取りの facade

### 4.1.3 完了条件

- UI / rescue / tests から新しい direct constructor が増えない
- 旧入口や重複検証が service 側へ戻らない
- architecture test で境界逸脱を即検知できる

## 5. Phase 1: 救済レーン実動画検証

### 5.1 目的

- 救済レーンの存在確認ではなく、運用上の副作用確認を行う。
- 特に通常動画の初動を壊していないかを先に見る。

### 5.2 確認対象

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
- `MainWindow.xaml.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Thumbnail/Docs/救済レーン実動画確認チェックリスト_2026-03-12.md`
- `Thumbnail/Docs/手動再試行運用手順.md`

### 5.3 最優先で見る項目

1. 通常動画では `thumbnail-timeout`、`thumbnail-recovery`、`thumbnail-rescue` が不要に出ないこと
2. 重動画では通常レーン `10` 秒 timeout 後に rescue へ handoff されること
3. 通常失敗動画では failure handoff が 1 回だけ発火すること
4. repair 対象動画だけで `thumbnail-repair probe` / `repair` が出ること
5. 手動等間隔サムネイル作成では stale `ERROR` マーカー削除後に rescue へ入ること
6. error プレースホルダ表示動画が通常キューへ戻されず rescue に隔離されること

### 5.4 完了条件

- 通常動画の初動劣化なしを説明できる
- timeout handoff と failure handoff の差を説明できる
- repair 条件を一般条件で言語化できる
- `ERROR` マーカー削除の発火箇所を説明できる

## 6. Phase 2: Queue 観測の最小補強

### 6.1 方針

- 新しい観測基盤は足さない。
- 実動画検証で迷った箇所だけをログで補う。

### 6.2 補う候補

- timeout handoff の投入元と投入先
- failure handoff の失敗理由
- repair を見送った理由
- `ERROR` マーカー削除の成否
- error プレースホルダ起点救済の件数

### 6.3 完了条件

- 実動画確認時に「なぜ救済へ行ったか」をログで追える
- hot path を広く重くしていない

## 7. Phase 3: `ERROR` 動画向け明示 UI

### 7.1 位置づけ

- このフェーズは新しい救済ロジックの追加ではない。
- 既存 rescue レーンへ、ユーザーが明示投入する入口を足すフェーズである。

### 7.2 未導入の導線

- `サムネ失敗` タブ
- `サムネイル救済処理` ボタン
- 右クリック `サムネイル救済...`

### 7.3 着手条件

- Phase 1 の実動画検証で、通常系を壊していない説明がつくこと
- 既存の手動再試行運用と役割衝突がないこと

## 8. Phase 4: UI テンポ改善

### 8.1 位置づけ

- `FilteredMovieRecs` 一本化や非同期再読込は価値が高い。
- ただし今は、救済レーンの副作用確認より優先しない。

### 8.2 注意

- 先に UI 更新経路を大きく触ると、救済系の影響切り分けが難しくなる。
- 救済系の説明可能性を先に固める。

## 9. Phase 5: 難読動画条件の棚卸し

### 9.1 方針

- 新分岐を増やす前に、現行条件を整理する。
- `ラ・ラ・ランド系` は個別特例ではなく、終端 OpenCV 救済を残す価値がある代表群として扱う。

### 9.2 整理対象

- repair が走った条件
- repair が走らなかった条件
- `No frames decoded` で救えた条件
- `No frames decoded` でも救えなかった条件
- `ERROR` マーカー固定へ落ちた条件

## 10. 今回見送るもの

- `future` からの大規模取り込み
- FailureDb の全面導入
- Worker 分離や IPC
- Coordinator 群の丸移植
- 個別動画名前提の新分岐追加

## 11. 受け入れ判断

- 救済レーン:
  - 通常動画の初動を壊していない
  - timeout handoff と failure handoff を区別して追える
  - repair 条件を一般条件で説明できる
  - `ERROR` マーカー削除の挙動を説明できる
- UI:
  - 明示 UI は既存 rescue レーンへ薄く載せる前提で設計できる
- Queue:
  - 最小ログで詰まり理由を追える

## 12. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.xaml.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Watcher\MainWindow.Watcher.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\救済レーン実動画確認チェックリスト_2026-03-12.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\手動再試行運用手順.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Docs\Implementation Plan_サムネイル救済処理_ERROR動画一括救済_2026-03-12.md`
