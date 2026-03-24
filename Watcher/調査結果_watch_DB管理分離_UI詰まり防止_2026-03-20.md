# 調査結果 watch / DB管理分離 UI詰まり防止 2026-03-20

## 1. 結論

- 分離は可能。
- ただし、最初にやるべき分離は「`watch` テーブルの物理分離」ではない。
- UI詰まり防止の本命は、`MainWindow.Watcher.cs` に残っている
  - watch制御
  - MainDB書き込み
  - UI反映
  - サムネキュー投入
  の同居をほどくこと。
- `watch` 設定そのものは WhiteBrowser 互換の都合もあるため、当面は MainDB 内の `watch` テーブル維持でよい。

## 2. 現状の整理

### 2.1 すでに分離できているもの

- 重い走査は `Task.Run(() => ScanFolderWithStrategyInBackground(...))` で UI スレッド外へ逃がしている。
- 新規 `movie` 登録は `InsertMovieTableBatch(...)` でバッチ化済み。
- サムネキューは QueueDB、失敗管理は FailureDb へ分離済み。
- `CheckFolderAsync(...)` は DB切替時のスナップショットも持っている。

### 2.2 まだ密結合のまま残っているもの

- `CheckFolderAsync(...)` が `MainVM` の表示状態、現在タブ、visible range、通知表示まで直接握っている。
- `TryAppendMovieToViewByPathAsync(...)` が MainDB読取と WPF `Dispatcher` 更新を1本で持っている。
- `FileChanged(...)` は `async void` で、Created 多発時に個別で
  - `MovieInfo` 生成
  - MainDB登録
  - UI反映
  - サムネ投入
  を直接進める。
- 変化あり時の最後は `FilterAndSort(..., true)` で全体再評価へ戻る。

## 3. UI詰まりの主因として見るべき箇所

### 3.1 watch処理そのものより「watch完了後のUI再評価」

- `CheckFolderAsync(...)` の末尾で `FolderCheckflg` が立つと `FilterAndSort(MainVM.DbInfo.Sort, true)` を呼ぶ。
- この経路は `SELECT * FROM movie` の再読込と一覧再構築に戻るため、変更件数が少なくても UI 側の仕事が大きい。
- つまり「watchとDB管理を分離する」だけでは不十分で、`watch -> UI全面再評価` の鎖を切る必要がある。

### 3.2 `FileChanged` の直列フルコース

- `FileChanged(...)` は `QueueCheckFolderAsync(...)` 系の coalesce に乗っていない。
- そのため Created が連打される環境では、`async void` の個別処理が並走しやすい。
- UIスレッドへ戻る量自体は限定的でも、CPU / I/O / ThreadPool 競合で体感テンポを落とし得る。

### 3.3 MainDB書き込み経路にメタ取得が混在

- `InsertMovieTable(...)` / `InsertMovieTableBatch(...)` は INSERT と同じ流れで `TryReadBySinkuDll(...)` を呼ぶ。
- これは「DB writer」と「重いメタ解析」がまだ分離されていない状態。
- UIスレッド直撃ではないが、watch全体の完了を遅らせ、最終的に UI 更新タイミングを後ろへ押す。

## 4. 分離はどこまで現実的か

### 4.1 推奨: 同一プロセス内で責務分離する

- これは現実的。
- `workthree` の方針とも合う。
- 具体的には次の4層へ分ける。

1. `WatchOrchestrator`
- 監視要求の受付、coalesce、visible-only 判定、DB切替中断判定だけ持つ。

2. `WatchMainDbWriter`
- 既存movie辞書の取得
- 新規movieのバッチINSERT
- 必要なら後追いUPDATE
  を担当する。

3. `WatchUiBridge`
- `PendingMovieRecs`
- 1件追加
- 最終refresh要求
  だけを担当する。

4. `WatchThumbnailEnqueueBridge`
- `QueueObj` 化と `TryEnqueueThumbnailJob(...)` 呼び出しだけを担当する。

### 4.2 条件付きで有効: single writer 化

- MainDB への書き込みを `Channel` か専用キューで単一ライターへ寄せる案は有効。
- 特に `FileChanged` と `CheckFolderAsync` が別々に INSERT へ入る現状には効く。
- ただし「DB writer を別スレッドにしただけ」で `FilterAndSort(..., true)` を残すと、UI詰まりは取り切れない。

### 4.3 慎重: 別プロセス化 / IPC 化

- 技術的には可能。
- ただしこのブランチ方針では、常駐サービス化や本格IPCは慎重項目。
- いまのボトルネックはそこまで行かなくても改善できる。
- よって第1候補にはしない。

### 4.4 非推奨: `watch` テーブルの物理分離

- UI詰まり防止の直接効果が薄い。
- WhiteBrowser 互換DBの理解コストも上がる。
- 設定保存先だけ別DBにしても、実際に重いのは `movie` 反映と UI 更新なので本丸を外す。

## 5. まず切るべき境界

### 5.1 最優先

1. `FileChanged(...)` を直接INSERT経路から外す
- `Created` は軽量イベントとしてキューへ積み、`CheckFolderAsync` 系と同じ入口へ寄せる。
- これで Created 多発時の多重並行を抑えられる。

2. `TryAppendMovieToViewByPathAsync(...)` を `WatchUiBridge` へ隔離する
- watch本体から `Dispatcher` 参照を消す。
- watch本体は「正式反映依頼を出した」までで止める。

3. `FilterAndSort(..., true)` の全面再読込を watch終端から外す
- 小規模時は差分反映だけで終える。
- 大規模時だけ dirty flag を立て、UIアイドル時または遅延タイマーで再評価する。

### 5.2 次点

4. `InsertMovieTableBatch(...)` から `Sinku` 取得を外す
- 初回 INSERT は最小列だけで通す。
- `container / video / audio / extra / movie_length補完` は後追い更新ジョブへ寄せる。

5. `existingMovieByPath` と UI snapshot 取得を service 化する
- `BuildCurrentViewMoviePathLookupAsync(...)`
- `BuildCurrentDisplayedMovieStateAsync(...)`
- `BuildCurrentVisibleMoviePathLookupAsync(...)`
  を `MainWindow` 直下から外す。

## 6. 段階案

### Phase 1

- `WatchUiBridge` 抽出
- `WatchMainDbWriter` 抽出
- `FileChanged` を要求キュー経由へ変更

期待効果:
- `MainWindow.Watcher.cs` から UI と DB の直結を減らせる。
- Created連打時の暴れ方を抑えられる。

2026-03-20 実装反映:
- `FileChanged` は直接 MainDB登録せず、`QueueCheckFolderAsync(CheckMode.Watch, ...)` へ合流するよう変更した。
- `TryAppendMovieToViewByPathAsync(...)` / pending placeholder 操作 / visible snapshot 取得は `Watcher/MainWindow.WatcherUiBridge.cs` へ寄せ、watch本体から `Dispatcher` 詳細を剥がし始めた。
- `InsertMoviesToMainDbBatchAsync(...)` / `BuildExistingMovieSnapshotByPath(...)` は `Watcher/MainWindow.WatcherMainDbWriter.cs` へ寄せ、watch本体から MainDB アクセス入口を分け始めた。
- `RenameThumb(...)` も `Watcher/MainWindow.WatcherRenameBridge.cs` へ移し、rename 起点の DB / サムネ / bookmark 更新責務を watch 側へ寄せ始めた。
- `FileRenamed` は直接 `RenameThumb(...)` を叩かず、rename 要求キューへ積んで単一ランナーで順番に流す形へ変えた。
- `FileChanged` / `FileRenamed` は共通の watch event queue へ積み、イベントハンドラから重い処理を外した。
- watch event queue 自体も `Watcher/MainWindow.WatcherEventQueue.cs` へ寄せ、`MainWindow.Watcher.cs` には入口だけを残し始めた。
- `FileChanged` / `FileRenamed` / `RunWatcher` / `CreateWatcher` / watcher作成判定は `Watcher/MainWindow.WatcherRegistration.cs` へ寄せ、監視登録の責務を本流処理から分けた。
- `pendingNewMovies` の flush も `Watcher/MainWindow.WatchScanCoordinator.cs` へ寄せ、`CheckFolderAsync` から local function の大塊を外し始めた。
- これで `MainWindow.Watcher.cs` 側は「scan / per-file 判定 / 終端制御」、coordinator 側は「DB反映 / 小規模UI反映 / enqueue 調停」と役割が見え始めた。
- さらに per-file の `new/existing` 分岐も `ProcessScannedMovieAsync(...)` として `Watcher/MainWindow.WatchScanCoordinator.cs` へ寄せた。
- `CheckFolderAsync` 側は probe 計測と folder 集計中心へ寄り、watch 本流の見通しが一段良くなった。

### Phase 2

- watch終端の `FilterAndSort(..., true)` を dirty + debounce 化
- 小規模差分は個別追加だけで閉じる

期待効果:
- UI詰まり防止として一番効く。

2026-03-20 実装反映:
- `CheckMode.Watch` の終端 reload は `350ms` の debounce で最新1回へ圧縮した。
- `DB切替` 時は pending reload を取り消し、旧DB向け遅延実行を止めるようにした。

### Phase 3

- `Sinku` 後追い化
- 必要なら single writer 化

期待効果:
- watch完了待ちをさらに短くし、初動テンポを改善できる。

### Phase 4

- それでも重ければ sidecar / IPC を再検討

期待効果:
- プロセス分離による干渉低減。
- ただし導入コストと切替複雑化は大きい。

## 7. 判断

- 「watchとDB管理の分離」は可能。
- ただし意味のある分離は
  - watch設定保存先の分離
  ではなく
  - watch orchestration
  - MainDB writer
  - UI bridge
  - enqueue bridge
  の分離である。
- UI詰まり防止だけを目的にするなら、まずは同一プロセス内の責務分離で十分戦える。
- 本命は `FilterAndSort(..., true)` の扱い見直しであり、ここを触らずに大がかりな別プロセス化へ進むのは順番が悪い。

## 8. 推奨次アクション

1. `FileChanged` を `QueueCheckFolderAsync` 系へ寄せる設計メモを先に作る
2. `WatchUiBridge` の最小インターフェースを定義する
3. watch終端の全面 `FilterAndSort(..., true)` を dirty 化する実装計画を切る

2026-03-20 更新:
- 上の 1〜3 は着手済み。
- `per-file 判定` も `WatchScanCoordinator` へ寄せ始めた。
- 次の本命は `CheckFolderAsync` にまだ残る `visible-only gate / zero-byte / first-hit 通知 / final queue flush` を、どこまで coordinator 側へ寄せてもテンポと可読性を落とさないか見極めること。

## 9. 左ドロワー開中の watch 抑制

- 左ドロワーを開いている間は、`EverythingPoll` と `Created` 由来の watch 新規流入だけ抑制する。
- 抑制中に来た watch 仕事は `deferred` フラグへ集約し、ドロワーを閉じた時に `QueueCheckFolderAsync(CheckMode.Watch, "ui-resume:left-drawer")` を 1 回だけ流す。
- `Renamed` は全量走査で代替できず、DB/サムネ整合に直結するため抑制対象へ入れない。
- 既に走り始めた `CheckFolderAsync` は途中キャンセルしない。入口だけ止めて、実行中ジョブは自然完走させる。
