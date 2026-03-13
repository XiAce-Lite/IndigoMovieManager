# Implementation Plan 本exe高速スクリーナー化と救済exe完全分離 2026-03-14

最終更新日: 2026-03-14

変更概要:
- 本exeを「高速スクリーナー」に寄せる方針を明文化
- 本exe内 retry / rescue lane を最終的に撤去する前提へ整理
- `FailureDb` を救済対象管理の受け皿として使う方針を追加
- 救済exeを「1本ずつ最後まで完遂する職人プロセス」として定義
- 本exeから削る責務一覧と `FailureDb` 状態遷移を固定

## 1. 目的

- `workthree` 本線の最優先は、通常動画の一覧、キュー投入、サムネイル初動の体感テンポである。
- 難物動画の完遂責務を本exeへ残したままだと、通常系 hot path に timeout、retry、repair、救済分岐が積み上がりやすい。
- そのため本書では、次の分離を正式案として整理する。
  1. 本exeは速く試し、速く見切る
  2. 失敗は `FailureDb` へ append する
  3. 救済exeが 1 本ずつ最後まで完遂する

## 2. 結論

- 最終形は、本exeから retry と rescue lane を外す方がよい。
- ただし順番を誤ると取りこぼしが増えるため、次の順で進める。
  1. `FailureDb` 最小実装を `workthree` へ入れる
  2. 救済exeの lease 制御と状態遷移を完成させる
  3. 本exeの rescue lane を停止する
  4. 本exeの retry / repair / handoff 分岐を削る
- 先に救済受け皿を作り、その後で本exeを痩せさせる。逆順は採らない。

## 3. 役割分担

### 3.1 本exeの役割

- 動画 1 件に対して短い時間予算で通常サムネイル作成を 1 回だけ試す
- 成功時のみ jpg と DB/UI 更新を行う
- 失敗時は `FailureDb` へ append し、`pending_rescue` へ遷移させる
- 通常系 UI と QueueDB のテンポを守る

### 3.2 救済exeの役割

- `FailureDb` から救済対象を lease 取得する
- 1 本ずつ固定順で最後まで試す
- 全試行を `FailureDb` へ append する
- 成功時のみ本体と同じ出力先へ jpg を保存し、必要な DB 更新を行う
- 失敗時は `gave_up` または次回再挑戦可能状態へ戻す

### 3.3 `FailureDb` の役割

- 失敗ログ
- 救済待ちキュー
- 救済中 lease 管理
- 成功 / 断念の結果記録

## 4. 本exeから削る責務一覧

以下は最終的に本exeから外す。

1. 通常失敗後の rescue lane handoff
2. 通常レーン timeout 後の rescue lane handoff
3. in-proc rescue worker 常駐
4. rescue 専用 engine 順切替
5. repair probe / repair / remux
6. source override を使った再実行
7. 長い retry
8. 難物動画専用の重い失敗解析

## 5. 本exeに残す責務一覧

以下は本exeに残す。

1. QueueDB から通常ジョブを受ける
2. 最速寄り engine で 1 回だけ試す
3. 短い timeout で見切る
4. 成功時の jpg 保存
5. MainDB / UI 反映
6. `FailureDb` への最小 append
7. `pending_rescue` 遷移

## 6. 救済exeに寄せる責務一覧

以下は救済exeへ寄せる。

1. 救済対象の lease 取得
2. engine 総当たり
3. repair / remux
4. source override 再実行
5. 長時間実行
6. 詳細な失敗分類
7. 詳細ログと比較材料の記録
8. DLL 分離実行フォルダの利用

## 7. `FailureDb` 最小スキーマ方針

初版は「1 動画 1 行」ではなく「1 試行 1 行 append」を基本とする。

最低限ほしい列:

- `FailureId`
- `MainDbPathHash`
- `MoviePath`
- `MoviePathKey`
- `TabIndex`
- `Lane`
- `AttemptGroupId`
- `AttemptNo`
- `Status`
- `LeaseOwner`
- `LeaseUntilUtc`
- `Engine`
- `FailureKind`
- `FailureReason`
- `ElapsedMs`
- `SourcePath`
- `RepairApplied`
- `ResultSignature`
- `ExtraJson`
- `CreatedAtUtc`

補足:

- `Lane` は `normal` / `rescue` を想定する
- `Status` は後述の状態遷移で使う
- `AttemptGroupId` は「同じ動画の一連の救済束」を追うために使う
- `ResultSignature` は「何で成功したか」の比較材料として使う

## 8. `FailureDb` 状態遷移

初版は次の状態を使う。

- `pending_rescue`
- `processing_rescue`
- `rescued`
- `gave_up`
- `skipped`

### 8.1 基本遷移

1. 本exe失敗
2. `FailureDb` へ append
3. `pending_rescue` を付与
4. 救済exeが lease 取得
5. `processing_rescue` へ更新
6. 成功なら `rescued`
7. 全手順失敗なら `gave_up`

### 8.2 例外遷移

- 対象ファイル消失: `skipped`
- lease 期限切れ: `processing_rescue` から再度 `pending_rescue`
- アプリ停止 / プロセス異常終了: lease 期限切れ後に `pending_rescue`

### 8.3 状態遷移の意図

- `pending_rescue` は「まだ誰も触っていない救済待ち」
- `processing_rescue` は「今まさに救済中」
- `rescued` は「救済成功」
- `gave_up` は「今回定義した全手順を試し切った」
- `skipped` は「入力前提を満たさず救済対象から外した」

## 9. lease 制御方針

- 救済exeは `FailureDb` から 1 件だけ lease 取得する
- 同時に複数救済exeが動いても、同一動画を二重取得しない
- lease 期限は長めでよいが、ハートビート延長を持つ
- 救済exe異常終了時は lease 期限切れで再取得可能にする

初版の原則:

- 1 worker = 1 movie
- 1 lease = 1 movie
- 1 success update = 1 final state update

## 10. DLL / ネイティブ依存の扱い

- DLL ロックや更新中競合があるなら、救済exeはセッション専用フォルダへコピーして起動してよい
- コピー対象は、救済exe本体、依存 DLL、必要なら ffmpeg 系 DLL 一式に絞る
- 実行フォルダ名には version と hash を含め、混在を避ける
- 終了後の掃除は best effort とし、掃除失敗を本体成功判定より優先しない

## 11. 本exeの処理方針

本exeの通常作成は次で固定する。

1. QueueDB から通常ジョブ取得
2. 最速寄り engine を選ぶ
3. 短い timeout で 1 回だけ試す
4. 成功なら通常完了
5. 失敗なら `FailureDb` append
6. `pending_rescue` を記録して終わり

重要:

- 本exeは失敗の「完遂責務」を持たない
- 本exeは「高速選別責務」を持つ
- 通常動画を守るため、重い再挑戦は持たない

## 12. 救済exeの処理方針

救済exeの初版は次で固定する。

1. `FailureDb` から 1 件 lease 取得
2. `processing_rescue` へ更新
3. `direct`
4. `engine switch`
5. `repair probe`
6. `repair/remux`
7. `source override retry`
8. 成功なら `rescued`
9. 全失敗なら `gave_up`

重要:

- 各手順の成否を全部 append する
- 比較可能性を優先する
- UI 応答より完遂を優先する

## 13. 実装フェーズ

### Phase 1: `FailureDb` 最小導入

対象:

- `fork` 側 `FailureDb` 最小一式
- `workthree` 側 Queue / Thumbnail からの append 接続

完了条件:

- 本exe失敗時に `FailureDb` へ 1 試行 1 行 append できる
- `pending_rescue` を記録できる

### Phase 2: 救済exe最小導入

対象:

- 救済exe プロジェクト
- `FailureDb` lease 制御
- 1 本処理フロー

完了条件:

- `pending_rescue -> processing_rescue -> rescued/gave_up` が成立する
- 同一動画の二重救済が起きない

### Phase 3: 本exe rescue lane 停止

対象:

- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
- 通常失敗 handoff
- timeout handoff

完了条件:

- 本exe内 rescue worker が起動しない
- 失敗は `FailureDb` へ流れる

### Phase 4: 本exe retry / repair 削減

対象:

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/ThumbnailCreationService.cs`
- `Thumbnail/Engines/ThumbnailEngineRouter.cs`

完了条件:

- 本exeが長い retry と repair を持たない
- 通常動画の hot path が短くなる

## 14. リスク

- `FailureDb` 導入前に rescue lane を削ると、取りこぼしが増える
- 救済exeと本exeが同じ jpg を同時更新すると競合する
- 成功時の MainDB 更新責務を曖昧にすると、不整合が残る
- `gave_up` 条件を甘くすると、無限再救済になりやすい

## 15. 採用判断

この案は、次を満たすなら採用価値が高い。

1. 通常動画の体感テンポが改善する
2. 難物動画の完遂率が落ちない
3. 失敗理由と救済履歴を後追いできる
4. 本exeと救済exeの責務境界を説明できる

## 16. 参照ファイル

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailCreation.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\ThumbnailCreationService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Thumbnail\Engines\ThumbnailEngineRouter.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.Queue\QueueDb\QueueDbService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDebugDbService.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureDebugDbSchema.cs`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\src\IndigoMovieManager.Thumbnail.Queue\FailureDb\ThumbnailFailureRecord.cs`
