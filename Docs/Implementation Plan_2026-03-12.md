# Implementation Plan

## 1. 目的

- 本書は「未導入の取り込み計画」ではなく、`workthree` にすでに入っている救済系を検証し、仕上げるための計画書である。
- いまの主題は `future` から何を持ち込むかではない。
- 主題は、救済レーン、repair、Watcher、`ERROR` マーカー削除が通常動画のテンポを壊さず動いているかを固めることである。

## 2. 現在の前提

- `workthree` は UI を含む高速化本線である。
- 救済系の主要要素は、すでに `workthree` 側へ入っている。
- したがって次にやるべきことは、新しい大きな取り込みではなく、導入済み要素の実動画検証、ログ確認、条件棚卸しである。
- 判断軸は次の 3 点で固定する。
  1. 通常動画の初動を壊していないか
  2. 救済系の handoff と repair 条件が説明可能か
  3. 失敗時にログだけで挙動を追えるか

## 3. 結論

- 最優先は、救済レーンの実動画検証である。
- UI テンポ改善は価値が高いが、いまは次点に回す。
- Queue 観測は追加実装を広げず、見える化不足の穴だけを埋める。
- 難読動画条件は新分岐追加より先に、現行の一般条件を棚卸しする。

## 4. 優先順位

| 優先 | 重点 | 目的 | 今回の扱い |
|---|---|---|---|
| P1 | 救済レーン実動画検証 | 通常動画の初動を壊さず救済が流れるか確認する | 最優先で検証計画を固める |
| P2 | UI テンポ改善 | 一覧更新と再読込の体感を改善する | 今回は次点。救済系の副作用確認後に着手 |
| P3 | Queue 観測の最小補強 | 救済、通常、Watcher の絡みをログで追えるようにする | 新機能追加ではなく、必要ログだけ補う |
| P4 | 難読動画条件の棚卸し | repair 条件と `No frames decoded` 系の実測を整理する | 新分岐追加は保留 |

## 5. Phase 1: 救済レーン実動画検証

### 5.1 目的

- 導入済みの救済系が、通常動画のテンポを壊さず動いているかを確認する。
- rescue lane の存在自体ではなく、handoff、repair、marker 制御が狙い通りかを固める。

### 5.2 最優先で見る観点

- 通常動画の初動
  - 救済系が有効でも、最初の数件表示や通常キュー開始が鈍っていないか
- `10` 秒 timeout 後の handoff
  - 通常側で詰まった後に、救済側へ渡る条件とタイミングが意図通りか
- repair 発火条件
  - repair が必要な時だけ走り、通常動画や軽い失敗へ広がっていないか
- `ERROR` マーカー削除
  - 手動再試行や救済再投入で stale marker を正しく外せるか
  - 自動系では失敗固定動画を無限再投入していないか

### 5.3 確認対象

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Thumbnail/Engines/FfmpegOnePassThumbnailGenerationEngine.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`

### 5.4 実動画検証の確認項目

1. 通常動画のみの投入で、救済レーンが余計に割り込まず初動が保たれること
2. rescue 対象動画で、通常経路から `10` 秒 timeout 後に handoff されること
3. repair 対象外の動画で、repair が発火しないこと
4. repair 対象動画で、発火条件とログが一致すること
5. `ERROR` マーカー付き動画が自動経路で無限再投入されないこと
6. 手動再試行時だけ marker 削除が先に走ること

### 5.5 完了条件

- 通常動画の初動劣化なしを説明できる
- handoff 条件をログと実動画結果で説明できる
- repair 発火条件を一般条件として言語化できる
- marker 削除の手動/自動の差を説明できる

## 6. Phase 2: UI テンポ改善

### 6.1 位置づけ

- `FilteredMovieRecs` 一本化や非同期再読込は価値が高い。
- ただし今は、救済レーンの副作用確認が先である。
- 本フェーズは救済系の挙動が固まった後に着手する。

### 6.2 次点で見る項目

- `FilteredMovieRecs` を表示正に寄せる整理
- 非同期再読込時の巻き戻り防止
- 検索、並び替え、タブ切替時の再代入コスト削減

### 6.3 注意

- 救済系の検証途中で UI 更新経路を大きく変えると、因果が切り分けにくくなる。
- 先に救済系、次に UI の順を守る。

## 7. Phase 3: Queue 観測の最小補強

### 7.1 方針

- Queue 観測は「追加実装」ではなく「見える化不足だけ補う」で進める。
- すでに救済、通常、Watcher が絡んでいるため、大きな観測基盤追加は避ける。

### 7.2 補うべき穴

- 通常レーンから救済レーンへ handoff した瞬間
- repair 発火の理由
- rescue 候補が marker で抑止されたかどうか
- timeout で待ったのか、即移管したのか
- queue 停滞が rescue 混在由来か通常混雑由来か

### 7.3 完了条件

- 実動画検証で迷った箇所を、追加ログだけで追える
- 観測追加が hot path を広く重くしていない

## 8. Phase 4: 難読動画条件の棚卸し

### 8.1 方針

- 難読動画条件は一般条件の棚卸しだけに絞る。
- 新しい分岐を増やす前に、現行の repair 条件と `No frames decoded` 系の実測結果を整理する。

### 8.2 整理対象

- repair が走った条件
- repair が走らず失敗した条件
- `No frames decoded` で救えた条件
- `No frames decoded` でも救えなかった条件
- `ERROR` マーカー固定に落ちた条件

### 8.3 今はやらないこと

- 個別動画向けの新分岐追加
- true near-black 系の本格反映
- 大きい retry policy 拡張
- 実験線由来ロジックの丸取り

## 9. 今回見送るもの

- `future` からの大規模取り込み計画
- Worker 分離、IPC、管理者テレメトリの拡張
- FailureDb の全面展開
- Coordinator 群の全面移植
- SWF 系や周辺ドキュメント一式の反映

理由は共通で、現時点で重要なのは新機能追加ではなく、導入済み救済系の安定確認だからである。

## 10. 受け入れ判断の条件

- 救済系:
  - 通常動画の初動を壊していない
  - `10` 秒 timeout 後の handoff を追える
  - repair 発火条件を一般条件で説明できる
  - `ERROR` マーカー削除の挙動を手動/自動で説明できる
- UI:
  - 救済検証が落ち着くまでは大きく触らない
- Queue:
  - 必要最小限のログで詰まり原因を追える
- 難読動画:
  - 新分岐を増やさず、現行条件の棚卸しを先に終える

## 11. 次に見るファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailQueue.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Watcher\MainWindow.Watcher.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\FfmpegOnePassThumbnailGenerationEngine.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\ThumbnailQueueProcessor.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\MainWindow.xaml.cs`
