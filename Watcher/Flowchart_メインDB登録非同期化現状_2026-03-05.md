# フローチャート（メインDB登録 非同期化の現状 / 2026-03-05 更新）

## 1. いまの全体像（Everything分岐 + 一本化）

```mermaid
flowchart TD
    A["トリガー"];
    A --> B1["FileSystemWatcher.Created"];
    A --> B2["監視更新要求 Auto/Watch/Manual"];
    A --> B3["Everythingポーリング RunEverythingWatchPollLoopAsync"];
    B3 -->|ShouldRunEverythingWatchPoll=Yes| B2;

    B1 --> C1["FileChanged async"];
    C1 --> D1["ファイル使用可能待ち await Task.Delay 最大10回"];
    D1 --> E1["Task.Run MovieInfo生成"];
    E1 --> U1["MainDB登録 1件 InsertMovieToMainDbAsync"];

    B2 --> C2["QueueCheckFolderAsync"];
    C2 --> D2["要求coalesce + 単一ランナー ProcessCheckFolderQueueAsync"];
    D2 --> E2["CheckFolderAsync"];
    E2 --> F2["Task.Run ScanFolderWithStrategyInBackground"];
    F2 --> G2{"走査戦略"};
    G2 -->|Everything| H2["IndexProviderFacade経由で候補取得"];
    G2 -->|Filesystem fallback| I2["Directory走査で候補取得"];
    H2 --> J2["新規movieパス列挙"];
    I2 --> J2;
    J2 --> K2{"新規movieごと"};
    K2 --> L2["Task.Run MovieInfo生成"];
    L2 --> M2["pendingNewMoviesに蓄積"];
    M2 --> N2{"flush条件"};
    N2 -->|到達| U2["MainDB登録 バッチ InsertMoviesToMainDbBatchAsync"];
    E2 --> Z2["走査終了後 FilterAndSort 変化あり時"];

    U1 --> U3["TryAppendMovieToViewByPathAsync Task.Run GetData + Dispatcher"];
    U2 --> U4{"小規模モード20件以下?"};
    U4 -->|Yes| U3;
    U4 -->|No| U5["UI反映は最後にまとめる"];
    U3 --> U6["RemovePendingPlaceholder"];
    U5 --> U6;
    U6 --> U7["TryEnqueueThumbnailJob"];
```

## 2. Everything分岐と一本化ポイント

- `CheckFolderAsync` の分岐点は `ScanFolderWithStrategyInBackground`。
  - `Everything` 利用可能: `IndexProviderFacade` 経由で候補取得。
  - 利用不可/対象外: `Filesystem fallback`（Directory走査）へ切替。
- 分岐後は、どちらも `NewMoviePaths` を同じ後段へ渡す。
  - `MovieInfo` 生成（`Task.Run`）
  - MainDB登録（`InsertMoviesToMainDbBatchAsync`）
  - UI反映（小規模時 `TryAppendMovieToViewByPathAsync`）
  - サムネキュー投入（`TryEnqueueThumbnailJob`）
- `FileChanged`（Createdイベント）経路も、後段は同じ思想で
  - MainDB登録（`InsertMovieToMainDbAsync`）
  - UI反映（`TryAppendMovieToViewByPathAsync`）
  - サムネキュー投入（`TryEnqueueThumbnailJob`）

## 3. まだUI詰まりに効く残課題

- `DataRowToViewData` 自体はUIスレッド実行（画像パス探索などが重い）。
- `FilterAndSort` は変更発生時に全体再評価が走る。
- `FileChanged` が高頻度連打される環境では、`async void` イベント処理が多重並行になる。
