# IndigoMovieManager Database Spec

最終更新日: 2026-03-28

## 1. この文書の役割

この文書は、「どの DB を見れば何が分かるか」を見分けるための案内です。

このプロジェクトには、役割の違う DB が複数あります。
最初にそこを分けて理解すると、調査がかなり楽になります。

## 2. 最初に覚えること

このアプリでは、主に 3 種類の DB を使います。

1. メイン DB
2. QueueDB
3. FailureDb

「動画一覧の正本」と「裏で動く処理用 DB」は別です。

## 3. メイン DB

### 3.1 概要

- 形式: `*.wb`
- 役割: 動画一覧、履歴、設定などの正本
- 前提: WhiteBrowser 互換を壊さない

最初の理解

- 画面に出る基本情報は、まずこの DB を起点に見る
- ただしサムネイル常駐処理そのものは、この DB だけで完結しない

### 3.2 主なテーブル

- `movie`
  - 動画一覧の中心
- `bookmark`
  - ブックマーク情報
- `history`
  - 履歴
- `findfact`
  - 検索関連の集計
- `watch`
  - 監視フォルダ設定
- `system`
  - DB ごとの設定値

### 3.3 こういう時に見る

- 一覧に動画が出ない
- watch 設定が反映されない
- DB ごとの設定がおかしい

## 4. QueueDB

### 4.1 概要

- 形式: `*.queue.imm`
- 保存先: `%LOCALAPPDATA%\{AppName}\QueueDb\`
- 役割: サムネイル生成待ちジョブの管理

最初の理解

- 「今からサムネイルを作る仕事一覧」を持つ DB
- メイン DB とは別物
- UI を止めずに裏で処理するための土台

### 4.2 何が入るか

- どの動画を処理するか
- どのタブ向けか
- 誰が処理中か
- リース期限
- 状態遷移

### 4.3 こういう時に見る

- サムネイルが作られない
- ジョブが詰まっていそう
- 同じ動画が何度も再投入されていそう

## 5. FailureDb

### 5.1 概要

- 形式: `*.failure.imm`
- 保存先: `%LOCALAPPDATA%\{AppName}\FailureDb\`
- 役割: 失敗したサムネイル生成と救済状態の管理

最初の理解

- 通常生成で失敗したものを隔離して管理する DB
- RescueWorker がここを見て救済する
- 重い失敗を通常キューへ無限に戻さないための仕組みでもある

### 5.2 主な見方

見るポイント

- `Status`
- `FailureReason`
- `Engine`
- `AttemptCount`
- `LastAttemptUtc`

状態のざっくり意味

- `pending_rescue`
  - 救済待ち
- `processing_rescue`
  - 救済中
- `rescued`
  - 救済成功
- `gave_up`
  - これ以上は救済しない

### 5.3 こういう時に見る

- 通常生成は失敗しているのに UI で理由が見えない
- RescueWorker が動いたか確認したい
- `ERROR` 動画が戻らない理由を知りたい

## 6. ログや保存先との関係

DB だけでなく、ログと合わせて見ると切り分けしやすいです。

よく使う場所

- ログ
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\`
- QueueDB
  - `%LOCALAPPDATA%\{AppName}\QueueDb\`
- FailureDb
  - `%LOCALAPPDATA%\{AppName}\FailureDb\`

`{AppName}` はブランド設定で変わることがあります。

## 7. 症状別にどこを見るか

### 7.1 一覧に動画が出ない

優先して見る

1. メイン DB
2. watch 設定
3. Watcher のログ

### 7.2 サムネイルが出ない

優先して見る

1. QueueDB
2. FailureDb
3. サムネイル関連ログ

### 7.3 一度失敗した動画が戻ってこない

優先して見る

1. FailureDb の `Status`
2. RescueWorker 側ログ
3. `ERROR` マーカーや UI 反映側

## 8. 誤解しやすい点

- メイン DB だけ見ても、サムネイル問題は全部は分からない
- QueueDB は「正本の動画一覧」ではない
- FailureDb は「単なるエラー履歴」ではなく、救済レーンの状態管理でもある
- `*.wb` と `*.queue.imm` と `*.failure.imm` は役割が違う

## 9. 実装上の注意

- メイン DB は WhiteBrowser 互換を崩さない前提で扱う
- 読み取りと書き込みの責務を混ぜすぎない
- QueueDB / FailureDb の状態を理解せずに rescue 導線を変えない

## 10. 次に読む文書

DB の役割が分かったら、次はこの順がおすすめです。

1. **[ProjectOverview_2026-03-28.md](../Gemini/ProjectOverview_2026-03-28.md)**
2. **[Architecture_2026-02-28.md](Architecture_2026-02-28.md)**
3. `Watcher` や `Thumbnail` 配下の関連資料

## 11. 最後に

最初からテーブル定義を全部覚えなくて大丈夫です。

まずは

- 一覧ならメイン DB
- 生成待ちなら QueueDB
- 失敗と救済なら FailureDb

この切り分けだけ覚えてください。

それだけで、かなり迷いにくくなります。
