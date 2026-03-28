# フローチャート（メインDB登録 非同期化の現状 / 2026-03-05 更新）

## 1. いまの全体像（Everything分岐 + 一本化）

```mermaid
flowchart TD
    A["トリガー"];
    A --> B1["FileSystemWatcher.Created / Renamed"];
    A --> B2["監視更新要求 Auto/Watch/Manual"];
    A --> B3["Everythingポーリング RunEverythingWatchPollLoopAsync"];
    B3 -->|ShouldRunEverythingWatchPoll=Yes| B2;

    B1 --> C1["watch event enqueue"];
    C1 --> C1A["単一ランナー ProcessWatchEventQueueAsync"];
    C1A -->|Created| D1["ファイル使用可能待ち await Task.Delay 最大10回"];
    D1 --> E1["zero-byte確認 / 軽量ガード"];
    E1 --> C2;
    C1A -->|Renamed| E1A["RenameThumbAsync"];

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
    E2 --> Z2{"走査終了後 UI再読込"};
    Z2 -->|Watch| Z3["dirty + debounce 後に FilterAndSort(true)"];
    Z2 -->|Auto/Manual| Z4["即時 FilterAndSort(true)"];

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
- `FileChanged` / `FileRenamed` は、どちらも watch event queue へ積んでから処理する。
  - `Created` は file ready 待ちと zero-byte 判定の後で `QueueCheckFolderAsync(CheckMode.Watch, ...)` へ合流する。
  - `Renamed` は `RenameThumbAsync` を単一ランナーで順番に流す。
  - これで watcher イベントハンドラ自体は重い処理を持たない。
- `pendingNewMovies` の flush は `Watcher/MainWindow.WatchScanCoordinator.cs` へ寄せた。
  - `CheckFolderAsync` 側は「scanして flush を呼ぶ」形へ薄くし始めた。
  - flush 側で `MainDB登録 -> 小規模UI反映 -> placeholder解除 -> サムネ投入` をまとめて調停する。
- `new/existing` の per-file 判定も `ProcessScannedMovieAsync(...)` として `Watcher/MainWindow.WatchScanCoordinator.cs` へ寄せた。
  - `CheckFolderAsync` 側は probe 計測と folder 単位集計が主になった。
  - scan coordinator 側で `DB存在確認 -> view整合 -> missing thumb enqueue` を順番に握る。
- `CheckFolderAsync` 終端の全件 reload は、`CheckMode.Watch` の時だけ `dirty + debounce` で最新1回へ圧縮する。
  - `Manual` は即時反映のまま維持する。
  - `DB switch` 時は pending reload を取り消し、旧DB向けの stale 実行を止める。

## 3. まだUI詰まりに効く残課題

- `DataRowToViewData` 自体はUIスレッド実行（画像パス探索などが重い）。
- `FilterAndSort` は watch 連打時の即時実行は減ったが、実行1回あたりの全体再評価コストは依然として大きい。
- `FileChanged` の直書き経路は外れたが、watch終端の `FilterAndSort(..., true)` は依然として大きい。
