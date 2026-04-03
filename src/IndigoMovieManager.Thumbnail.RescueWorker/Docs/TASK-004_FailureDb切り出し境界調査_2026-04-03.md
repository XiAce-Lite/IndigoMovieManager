# TASK-004 FailureDb切り出し境界調査 2026-04-03

## 目的

`RescueWorker` が本当に必要としているものだけを残し、`Queue` 全体への依存を外せる境界を固定する。

結論から言うと、worker が欲しいのは `Queue` ではなく `FailureDb` である。
ただし `FailureDb` の中にも、DB 本体・保存先決定・path 正規化・host 注入の境界が混ざっているため、そこを先に分ける必要がある。

## 1. 切り出し候補型一覧

### FailureDb 側に置くべきもの

- `ThumbnailFailureRecord`
- `ThumbnailFailureKind`
- `ThumbnailFailureDbSchema`
- `ThumbnailFailureDbService`
- `ThumbnailFailureDbPathResolver`

### Contracts 側または共通契約側に寄せるもの

- `ThumbnailQueuePriority`
- `ThumbnailQueuePriorityHelper`
- `MainDbPathHash` / `MoviePathKey` の正規化規約

### 残留側に置くべきもの

- `ThumbnailQueueHostPathPolicy`
- `QueueDbService`
- `QueueDbPathResolver` のうち、Queue 専用の保存先決定
- `ThumbnailFailureRecorder`

## 2. 依存方向

### 現在の状態

- `RescueWorker` -> `Queue` -> `FailureDb`
- `FailureDb` -> `QueueDbPathResolver`
- `FailureDb` -> `ThumbnailQueueHostPathPolicy`

### 目標状態

- `RescueWorker` -> `FailureDb`
- `Queue` -> `FailureDb`
- `FailureDb` -> `Contracts`
- `FailureDb` -> 最小の path 正規化ユーティリティ
- `Host` -> `ThumbnailQueueHostPathPolicy`

## 3. worker が本当に必要としている型

worker の実際の要求は次の3群に集約できる。

1. `ThumbnailFailureDbService`
2. `ThumbnailFailureRecord`
3. `ThumbnailFailureKind`

これに加えて、worker が救済優先度を扱うなら `ThumbnailQueuePriority` 系は必要だが、これは `Queue` 所有にしない方がよい。
既に `Contracts` 側へ寄せられるなら、`FailureDb` はそこを参照するだけで足りる。

## 4. 最初の安全な実装順

### Step 1

`QueueDbPathResolver` から `GetMainDbPathHash8` と `CreateMoviePathKey` を分離する。
ここが `FailureDb` の最初の切り出し点になる。

2026-04-03 実装メモ:
- `ThumbnailPathKeyHelper` を `Contracts` へ追加し、`MainDbPathHash` / `MoviePathKey` の正規化規約を移した
- `ThumbnailFailureDbPathResolver` と `ThumbnailFailureDbService` は `QueueDbPathResolver` 直参照を外した

### Step 2

`ThumbnailFailureRecord` と `ThumbnailFailureKind` を `FailureDb` 側へ独立させる。
これは純データなので、振る舞いリスクが最も低い。

2026-04-03 実装メモ:
- `ThumbnailFailureRecord` と `ThumbnailFailureKind` は `Contracts` へ移し、`Queue` project 所有から外した
- これで worker / queue / test が同じ共有データ型を直接参照できる土台ができた

### Step 3

`ThumbnailFailureDbSchema` と `ThumbnailFailureDbService` を `FailureDb` 側へ寄せる。
この時点で `Queue` は append / lease / read の実装を持たなくてよくなる。

2026-04-03 実装メモ:
- `IndigoMovieManager.Thumbnail.FailureDb` project を追加した
- `ThumbnailFailureDbPathResolver / ThumbnailFailureDbSchema / ThumbnailFailureDbService` は新 project へ移した
- `ThumbnailQueueHostPathPolicy` は `Contracts` へ移し、`FailureDb -> Queue` の project 依存を避けた

### Step 4

`ThumbnailFailureDbPathResolver` から `ThumbnailQueueHostPathPolicy` 依存を抜き、保存先は host から注入する。
ここで初めて `FailureDb` が host 境界に対して薄くなる。

### Step 5

`Queue` 側の `ThumbnailFailureRecorder` と `RescueWorker` の参照を新 `FailureDb` へ付け替える。
最後に `RescueWorker` から `Queue` project reference を外す。

## 5. 判断

`FailureDb` の独立は「Queue 全体を分ける」話ではない。
切るべきなのは、worker が救済のために必要な最小の DB 契約だけである。

したがって、最初に守るべき順番は次の通り。

1. path 正規化の共有化
2. FailureDb の record / schema / service 分離
3. host path policy の外出し
4. worker 参照の Queue 解除

この順なら、今の体感テンポを壊さずに `TASK-004` を進められる。
