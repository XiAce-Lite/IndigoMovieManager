# Implementation Plan + tasklist（サムネイルjpg削除時の再作成漏れ対策 2026-03-04）

## 0. 背景
- 現状、`movie` テーブルは `movie_path` / `hash` を保持するが、サムネイルjpgの「存在状態」は保持していない。
- 再生成判定は実ファイル存在チェックで行う設計だが、`CheckMode.Watch`（Everything差分監視）では `changedSince` 条件により「動画更新がない削除ケース」を取りこぼすことがある。
- `EnqueueMissingThumbnailsAsync(...)` は実装済みだが、呼び出し導線がなく常時救済できていない。
- さらに、`EnqueueMissingThumbnailsAsync(...)` は DB 起点のため、「実フォルダにはあるが DB/表示に未反映」の動画は拾えない。
- そのため、`Watch` 差分が `0件` のまま続くケースでは、現在タブの実フォルダと DB/表示のズレを別経路で補正する必要がある。

## 1. 目的
- ユーザーがサムネイルjpgを削除した場合でも、一定時間内に自動で再作成キューへ再投入される状態にする。
- 既存のQueueDB運用（重複吸収・再起動復元）を壊さず、過剰投入を防ぐ。
- MainDB（`.wb`）スキーマを変更せずに解決する。
- 現在タブで監視中の実フォルダと DB/表示がズレた場合でも、手動更新なしで自己修復できる状態にする。

## 2. 再現条件（現状）
1. 監視が `CheckMode.Watch`（Everythingポーリング）で動作している。
2. 動画ファイル自体は更新されていない。
3. 対象タブのサムネイルjpgのみ手動削除する。
4. 差分候補に動画が出てこないため、再投入されない場合がある。

## 3. 方針（結論）
- `CheckFolderAsync` の通常スキャンとは別に、**低頻度の欠損救済パス**を追加する。
- 救済は既存 `EnqueueMissingThumbnailsAsync(...)` を利用し、実装重複を避ける。
- 実行条件を絞る（スロットル + 負荷判定）ことで、全件走査の連打を防止する。
- ただし `Manual` は利用者の明示要求なので、高負荷抑止の対象外として即時救済を優先する。対象は `手動更新` とメニューの `監視フォルダ更新チェック` の両方。
- あわせて、`Watch + Everything + 差分0件` の時だけ、`DB + 監視フォルダ + sub` 単位で最短60秒ごとの**低頻度フル再突合**を追加する。
- この再突合で「実フォルダにはあるが DB/表示に無い」動画を検知し、通常の登録・キュー投入経路へ戻す。

## 4. スコープ
- IN
  - `Watcher/MainWindow.Watcher.cs` への欠損救済スケジューリング追加
  - `Watcher/MainWindow.Watcher.cs` への Watch差分0件時フル再突合追加
  - ログ追加（救済開始/スキップ/投入件数）
  - 手動回帰手順の更新
- OUT
  - MainDBスキーマ変更
  - QueueDBスキーマ変更
  - サムネイル命名規則変更

## 5. 実装タスクリスト

| ID | 状態 | タスク | 主対象ファイル | 完了条件 |
|---|---|---|---|---|
| THUMB-DEL-001 | 完了 | 欠損救済の実行間隔を管理する状態を追加 | `Watcher/MainWindow.Watcher.cs` | `DB単位 + Tab単位` で最終救済時刻を保持できる |
| THUMB-DEL-002 | 完了 | 欠損救済の実行判定（スロットル）を追加 | `Watcher/MainWindow.Watcher.cs` | ポーリングごとに救済が連打されない |
| THUMB-DEL-003 | 完了 | `CheckFolderAsync` 終端で欠損救済を呼ぶ導線を追加 | `Watcher/MainWindow.Watcher.cs` | `CheckMode.Watch` / `CheckMode.Manual` で救済が発火する |
| THUMB-DEL-004 | 完了 | Queue高負荷時の救済抑止を追加 | `Watcher/MainWindow.Watcher.cs`, `Thumbnail/MainWindow.ThumbnailQueue.cs` | `Watch` 自動救済は active queue件数が閾値超過時にスキップし、`Manual` は明示実行を優先する |
| THUMB-DEL-005 | 完了 | 救済ログを追加 | `Watcher/MainWindow.Watcher.cs` | `start/skip/enqueued/elapsed` が `watch-check` に出る |
| THUMB-DEL-006 | 完了 | 手動運用手順に削除復旧確認を追記 | `Thumbnail/手動再試行運用手順.md`（または関連手順書） | 監視ポーリング中の削除復旧確認手順が明文化される |
| THUMB-DEL-007 | 完了 | 回帰確認を実施 | 手動回帰メモ | 自動スモーク/テストおよび実動画での削除復旧確認が完了している |
| THUMB-DEL-008 | 完了 | Watch差分0件時の実フォルダ再突合を追加 | `Watcher/MainWindow.Watcher.cs` | `Watch + Everything + 差分0件` でも現在タブの実フォルダとDB/表示のズレを低頻度で補正できる |

## 6. 実装メモ
- 救済対象タブはまず `CurrentTabIndex` に限定し、処理コストを抑える。
- 将来拡張として「全タブ救済（0..4,99）」はオプション化する。
- スロットル初期値は `60秒` を推奨（設定値化は次フェーズ）。
- 例外時は監視ループを止めず、ログだけ残して継続する。
- 欠損救済は DB 起点、フル再突合は実フォルダ起点と役割を分ける。
- フル再突合は `Watch` の軽さを壊さないよう、`Everything` 差分 `0件` の時だけ追加で走らせる。

## 7. 受け入れ基準（DoD）
- `CheckMode.Watch` 稼働中にサムネイルjpgを削除しても、一定時間内に再作成キューへ再投入される。
- Queue投入が過剰連打にならない（同一動画の短時間連続投入が抑止される）。
- 既存機能（通常監視、手動再作成、QueueDB永続化）に退行がない。
- 現在タブの監視フォルダで、実フォルダにはあるが DB/表示に無い動画があっても、`Watch` だけで一定時間内に補正される。

## 8. 手動回帰観点
1. 任意動画の対象タブjpgを削除する。
2. Everythingポーリング待機後、`watch-check` ログで救済処理が走ることを確認する。
3. `thumbnail-create-process.csv` に再作成成功行が出ることを確認する。
4. Queue高負荷時（大量処理中）は `Watch` の自動救済が抑止され、`Manual` では即時救済できることを確認する。
5. 現在タブの監視フォルダへ、DB/表示に無い動画を置いたまま `Watch` を待ち、`scan reconcile start` / `scan reconcile end` の後で表示に反映されることを確認する。

## 9. 検証コマンド
- 本体ビルド
  - `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m:1`
- 手動回帰補助
  - `pwsh -NoProfile -File .\Thumbnail\Test\run_queue_e2e_manual.ps1`

## 9.1 実行メモ（2026-03-04）
- `MSBuild.exe ... IndigoMovieManager_fork.sln ...` : 成功（0 warning / 0 error）
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build` : 成功（50 passed / 0 failed / 2 skipped）
- `pwsh -NoProfile -File .\Thumbnail\Test\run_queue_e2e_manual.ps1 -AutoSmokeSeconds 5` : 成功（自動起動・停止、ログ退避完了）
- 実動画での「サムネイルjpg削除 -> 自動再作成」確認 : 完了（2026-03-05）

## 9.2 実行メモ（2026-03-07）
- `MSBuild.exe ... IndigoMovieManager_fork.sln ...` : 成功（0 warning / 0 error）
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~WatchFolderFullReconcilePolicyTests|FullyQualifiedName~MissingThumbnailRescuePolicyTests|FullyQualifiedName~FileIndexReasonTableTests|FullyQualifiedName~FileIndexProviderFactoryTests|FullyQualifiedName~FileIndexProviderAbDiffTests|FullyQualifiedName~UsnMftProviderTests|FullyQualifiedName~ThumbnailParallelControllerTests"` : 成功（45 passed / 0 failed / 6 skipped）
- 実機ログ確認で、`Watch` 差分が `since=... scanned=0` を繰り返す一方、`scan reconcile` による補正が必要なケースを確認した。

## 10. リスクと対策
- リスク: 救済処理が重く、監視レスポンスを落とす
  - 対策: `Watch` だけスロットル + Queue負荷判定 + CurrentTab限定で段階導入し、`Manual`（`手動更新` / メニュー `監視フォルダ更新チェック`）は利用者の明示実行に限って優先する。
- リスク: 救済と通常監視の二重投入
  - 対策: 既存のデバウンス + QueueDB一意制約で吸収し、ログで投入理由を識別する。
