# AI向け 引き継ぎ Watcher責務分離 UI詰まり防止 2026-03-20

最終更新日: 2026-03-20

変更概要:
- `Watcher` 周辺の責務分離を、次のAIがそのまま継続できるよう整理した
- `watch event queue` 化から `WatchScanCoordinator` 抽出までの到達点を固定した
- `watch` 多発時の詰まりを抑えるための境界条件、件数平準化、左ドロワー抑制を追記した
- 直近の未完了ポイントと、ビルド確認時のロック事情を明記した
- `visible-only` 中は表示中動画を 200 件制限より先に優先し、表示中が deferred 側へ押し出されない形へ補正した
- deferred watch state と `Everything last_sync` は snapshot DB スコープへ固定し、DB切替またぎの再混入を防ぐ形へ補正した

## 1. この文書の目的

- この文書は、`workthree` ブランチで進めている `Watcher` の UI詰まり防止改修を、別AIが途中から再開するための引き継ぎ資料である。
- 主眼は `watch` テーブル分離ではなく、`MainWindow.Watcher.cs` に同居していた監視制御 / UI反映 / MainDB書き込み / サムネ投入の責務整理である。
- 2026-03-20 時点では、`CheckFolderAsync` の巨大メソッドを段階的に薄くする流れの途中にいる。

## 2. ここまでで終わったこと

### 2.1 監視イベント入口の薄化

- `Created` は直接 MainDB登録せず、`QueueCheckFolderAsync(CheckMode.Watch, ...)` へ合流済み
- `Renamed` は rename 要求キュー経由で単一ランナー処理へ変更済み
- `FileChanged` / `FileRenamed` は共通 `watch event queue` に積み、イベントハンドラから重い処理を外した

### 2.2 `Watcher` partial 分割

次の partial へ責務を分けた。

- `Watcher/MainWindow.WatcherRegistration.cs`
  - 監視登録、`FileChanged` / `FileRenamed` 入口
- `Watcher/MainWindow.WatcherEventQueue.cs`
  - Created / Renamed の共通 queue
- `Watcher/MainWindow.WatcherUiBridge.cs`
  - `Dispatcher` 境界、pending placeholder、view snapshot
- `Watcher/MainWindow.WatcherMainDbWriter.cs`
  - MainDB の既存movie辞書取得、batch insert
- `Watcher/MainWindow.WatcherRenameBridge.cs`
  - rename 起点の DB / サムネ / bookmark 更新
- `Watcher/MainWindow.WatchScanCoordinator.cs`
  - scan 中の new/existing 分岐と pending flush 調停

### 2.3 `CheckFolderAsync` の整理

- watch終端の `FilterAndSort(..., true)` は `CheckMode.Watch` 時だけ `dirty + debounce` 化済み
- `pendingNewMovies` の flush は `FlushPendingNewMoviesAsync(...)` として `WatchScanCoordinator` へ移した
- per-file の `new/existing` 分岐も `ProcessScannedMovieAsync(...)` として `WatchScanCoordinator` へ移した
- 現在の `MainWindow.Watcher.cs` は、folder scan の進行管理、visible gate、通知、probe 集計寄りになっている

### 2.4 直近の watch 負荷平準化

- `EverythingPoll` の増分同期は `strictly newer` 判定へ寄せ、同一時刻境界で同じ候補を取り続けるループを止めた
- watch 1回あたりの候補処理数は `200` 件上限へ揃えつつ、`visible-only` 中は表示中動画を優先して今回分へ残し、残りを deferred batch として次回へ回す形にした
- deferred batch が残っている回でも新規候補の再収集は継続し、`visible-only` 中の新しい visible 候補は旧 backlog と再マージしたうえで先に返す形へ補正した
- `missing-tab-thumb` は `0..4` の通常上側タブだけを対象にし、`tab=5` へ絶対通らない job を作らないようにした
- 左ドロワー表示中は `EverythingPoll` と `Created` 由来の watch 新規流入だけ抑制し、閉じた時に catch-up を 1 回だけ流す
- watch 由来の最終 UI 再描画は、変更が in-memory 一覧へ反映済みで起動時部分ロード中でもない時だけ `query-only` 再計算へ寄せ、必要時だけ full reload を維持する
- rename 後の一覧追従は DB 再読込へ戻さず、`MainVM.MovieRecs` を元に再検索・再整列して bookmark 再読込と合わせて軽く反映する

## 3. 現在の見取り図

### 3.1 `MainWindow.Watcher.cs` に残っている主責務

- `QueueCheckFolderAsync(...)`
- `ProcessCheckFolderQueueAsync(...)`
- `CheckFolderAsync(...)` の folder 単位ループ
- `visible-only gate`
- zero-byte 早期スキップ
- folder first-hit 通知
- folder 終端の queue flush
- watch終端 UI reload と欠損サムネ rescue 呼び出し

### 3.2 `WatchScanCoordinator` に移った主責務

- `FlushPendingNewMoviesAsync(...)`
  - `MainDB登録 -> 小規模UI反映 -> placeholder解除 -> missing-thumb enqueue`
- `ProcessScannedMovieAsync(...)`
  - `DB存在確認 -> new/existing 分岐 -> view整合 -> missing-thumb enqueue`

### 3.3 いま残っている注意点

- 左ドロワー抑制は「新規開始だけ止める」方針であり、実行中の `CheckFolderAsync` は途中キャンセルしない
- `Renamed` は DB / サムネ整合を壊しやすいため、左ドロワー抑制の対象外である
- deferred batch を持つ folder scan は、途中で `last_sync_utc` を進めず、最後の batch 完了時だけ進める前提で実装している
- deferred batch を抱えたまま再収集した時も、保存する `last_sync_utc` は state 側へ保持し、backlog 解消時だけ最終 cursor を保存する
- deferred batch と `last_sync_utc` は `snapshotDbFullPath + watchFolder + sub` 単位で扱い、旧DB由来の走査完了が新DBへ混ざらない前提で見る
- watch 系をさらに触る時は「visible-only gate」「deferred batch」「UI抑制」が同時に効いている前提を崩さないこと

## 4. 直近の編集ファイル

- `Watcher/MainWindow.Watcher.cs`
- `Watcher/MainWindow.WatcherEventQueue.cs`
- `Watcher/MainWindow.WatchScanCoordinator.cs`
- `Watcher/FileIndexIncrementalSyncPolicy.cs`
- `Views/Main/MainWindow.xaml`
- `Views/Main/MainWindow.xaml.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatchUiSuppressionPolicyTests.cs`
- `Watcher/Docs/Flowchart_メインDB登録非同期化現状_2026-03-05.md`
- `Watcher/調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md`
- `AI向け_現在の全体プラン_2026-04-07.md`

## 5. 次の一手

優先順は以下でよい。

1. `CheckFolderAsync` に残る `visible-only gate / zero-byte / first-hit 通知 / final queue flush` を、どこまで `WatchScanCoordinator` 側へ寄せても読みやすさを壊さないか判断する
2. deferred batch 上限 `200` がまだ重いか、実ログで確認し、必要なら `100` まで下げる
3. `watch event queue` の DTO と実行本体を、`MainWindow` 依存からさらに離して `WatcherEventDispatcher` 相当へ寄せる
4. watch起点の UI 再読込を、差分反映優先でさらに縮められる点があるか確認する

## 6. やってはいけないこと

- `Created` の直書き MainDB登録経路を復活させない
- `RenameThumb` をイベントハンドラ直呼びへ戻さない
- `FilterAndSort(..., true)` の即時全面再評価を watch 多発経路へ戻さない
- `WatchScanCoordinator` に UI `Dispatcher` 詳細を戻さない
- テンポ改善の名目で probe や理由ログを削りすぎない
- 左ドロワー抑制を「実行中 job の強制中断」へ拡張しない
- `tab=5` など通常タブ外へ `missing-tab-thumb` を再び投げない

## 7. 確認状況

### 7.1 直近で確認できたこと

- 2026-03-20 時点で、`MSBuild x64` は `.codex_build\\watch-ui-suppress\\` 出力で成功している
- `WatchUiSuppressionPolicyTests` を含む watch 系テストは成功している
- 既定の `bin\\x64\\Debug` 出力先は、実行中プロセス `IndigoMovieManager` が DLL を掴んでコピー段で失敗するケースがある
- これはコードコンパイルエラーではなく、出力先ロック起因である
- `git diff --check` は対象ファイルで問題なし

### 7.2 確認時の注意

- 非SDK前提の運用方針に合わせ、ビルド確認は `MSBuild` を使う
- アプリ実行中は output copy で失敗し得るので、必要ならユーザー実行中プロセスの有無を先に確認する
- `Compile` ターゲットだけでの確認は WPF 生成物条件によってノイズが多く、通常の最終確認には向かない

## 8. 参照すべき資料

- `Watcher/Docs/Flowchart_メインDB登録非同期化現状_2026-03-05.md`
- `Watcher/調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md`
- `AI向け_現在の全体プラン_2026-04-07.md`
- `AI向け_ブランチ方針_ユーザー体感テンポ最優先_2026-04-07.md`

## 9. いま次担当が最初に見るべき場所

1. `Watcher/MainWindow.Watcher.cs`
   watch 抑制、deferred batch、queue 入口、`CheckFolderAsync` 本体の残タスクが集中している
2. `Watcher/MainWindow.WatcherEventQueue.cs`
   `Created` と `Renamed` の入口制御、左ドロワー抑制時の defer が入っている
3. `Views/Main/MainWindow.xaml.cs`
   左ドロワー UI イベントから watch 抑制へ入る経路がある
4. `Watcher/調査結果_watch_DB管理分離_UI詰まり防止_2026-03-20.md`
   watch まわりの判断理由が時系列でまとまっている

## 10. 一言で言うと

- `Watcher` 分離はかなり前進した
- ただし本丸の `CheckFolderAsync` はまだ folder orchestration を抱えている
- しかも今は `visible-only gate`、`deferred batch`、`UI抑制` が重なっているので、次担当はその3つの整合を見ながら動く必要がある
- 次は「何を coordinator 側へ寄せると楽になり、何を残した方が読みやすいか」を見極める段階である
