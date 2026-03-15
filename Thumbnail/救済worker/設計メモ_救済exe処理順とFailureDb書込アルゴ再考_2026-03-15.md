# 設計メモ 救済exe処理順とFailureDb書込アルゴ再考 2026-03-15

最終更新日: 2026-03-15

変更概要:
- live で、親 reason が弱い `fixed / unclassified` 個体が `ffmpeg one-pass failed` を契機に `route-long-no-frames` へ途中昇格することを確認した
- 親 failure reason が弱い `fixed / unclassified` 個体を、最初の direct failure から既存 route へ途中昇格する方針と実装を追記した
- 次段は新 route 追加より先に、既存 route の分類語彙と repair gate を強化する方針を追記した
- `long-no-frames` へ timeout 系と `ffmpeg one-pass failed` 系を寄せる初手を実装した
- `repair` は route-aware gate へ上げ、`corrupt-or-partial` は文言が弱くても repair へ入る形にした
- `p4` の入口として、失敗束サマリを `RouteId / SymptomClass` 付きで読めるようにした
- 実データでは代表束の多くがまだ `fixed / unclassified` に残ることを確認した
- 次に強める対象は route 追加よりも、既存分類語彙と gate の底上げである
- `ClassifyRescueSymptom(...)` と `BuildRescuePlan(...)` の初版を実装し、救済exeへ症状別 route 骨格を入れた
- route は `FailureKind + FailureReason + MovieSizeBytes + 拡張子` の軽量情報から切る形で開始した
- 親行 `ExtraJson` と子行 `attempt_failed` に `RouteId / SymptomClass` を載せる書込に更新した
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\*` を読み、救済exeの順序を fixed 総当たりから症状分岐型へ見直した
- `opencv` は救済末尾維持のまま、既定 timeout を他 engine より長めに分ける方針を明文化した
- `FailureDb` は「親行を状態の正本、子行を失敗試行ログ」として書く方針を整理した

## 1. 目的

- 本体は `autogen` 単発で早期見切りまで到達した
- 残る論点は救済exeだけである
- そのため本書では、救済exeの
  - 処理順
  - 症状別ルート
  - `FailureDb` への書き方
  を現行実装より一段整理して残す

参照元:
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\要約シート_単一入力メディアのサムネイル処理要約_2026-03-14.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\処理一覧表_単一入力メディアのサムネイル処理分類_2026-03-14.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\判定ルール表_単一入力メディアのサムネイル判定ルール_2026-03-14.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\失敗分類表_単一入力メディアのサムネイル失敗分類_2026-03-14.md`
- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork\Thumbnail\スレッド_全動画サムネイル処理分類_2026-03-14\代表ケース集_単一入力メディアのサムネイル代表ケース_2026-03-14.md`

## 2. 結論

### 2.1 fixed 総当たりは初版としては十分、最終形としては弱い

- 現行救済exeは全動画に対して
  - `ffmpeg1pass -> ffmediatoolkit -> autogen -> opencv`
  を固定で回している
- これは比較しやすいが、資料群が強く言っている
  - `ultra-short-no-frames`
  - `long-no-frames`
  - `near-black`
  - `corrupt-or-partial`
  を分ける思想がまだ入っていない

### 2.2 次の段階は「症状を先に切ってから順序を選ぶ」

- 救済exeは今後
  - `症状分類`
  - `救済ルート選択`
  - `重い手を後ろへ寄せる`
  へ上げる
- ただし本体へ戻さず、救済exe内だけで完結させる

### 2.3 `opencv` は最後尾維持、timeout は長めに分ける

- `opencv` は `token` 非対応で hard timeout 問題が残っている
- それでも直ちに前へ出す理由はない
- 方針は次で固定する
  - `opencv` は最後尾維持
  - nominal timeout は他 engine より長めにする
  - 本当の hard timeout は別タスクで watchdog 化する

## 3. 現行実装のズレ

### 3.1 `No frames decoded` を 1 種類として扱いすぎている

- 資料側では `No frames decoded` を単独分類にしない
- 先に見るべきは
  - 超短尺か
  - 長尺か
  - 破損疑いか
  - 品質不足か
  である

### 3.2 `autogen` の価値が動画種別で違う

- 本体では `autogen` 単発が正しい
- しかし救済exeでは、全ケースで `autogen` の価値が同じではない
- とくに
  - 超短尺
  - near-black
  では `autogen` 系の救済が先頭寄りに来る価値がある
- 一方
  - 長尺 no-frames
  - index 破損疑い
  では `one-pass` と `repair` の方が先に来るべきである

### 3.3 `opencv` は「最後の別実装」として扱うべき

- `opencv` は seek 別実装として価値はある
- ただし
  - 本体で使わない
  - rescue でも最後
  - timeout 問題がまだある
  という位置づけが妥当である

## 4. 救済exeの症状分類

救済exeが lease 取得直後に決める分類を次で固定する。

### 4.1 `S-01 missing-or-unreadable`

- 入力が無い
- 開けない
- ロック等で読めない

処理:
- `skipped` または `gave_up`
- engine 総当たりしない

### 4.2 `S-02 ultra-short-no-frames`

- 親失敗 reason が `No frames decoded`
- かつ短尺寄り
- または既知の超短尺群

処理思想:
- `drain`
- `tiny seek`
- 先頭寄り候補

### 4.3 `S-03 long-no-frames`

- 親失敗 reason が `No frames decoded`
- かつ超短尺ではない

処理思想:
- `extract.onepass`
- 必要時だけ `decode.sequential`
- それでも弱い時だけ `repair`

### 4.4 `S-04 near-black-or-old-frame`

- 画像自体はある
- ただし代表サムネとして弱い

処理思想:
- `latest-bright`
- 候補フレーム見直し
- `one-pass` は後段

### 4.5 `S-05 corrupt-or-partial`

- `invalid data found`
- `moov atom not found`
- `frame decode failed`
- `find stream info failed`
- one-pass 実行は終わるのに出力が無い

処理思想:
- `repair.probe`
- `repair.remux`
- 修復後入力で再試行

## 5. 推奨ルート

### 5.1 `R-A long-no-frames`

対象:
- `S-03 long-no-frames`

順序:
1. `ffmpeg1pass`
2. `ffmediatoolkit`
3. `repair.probe`
4. `repair.remux`
5. 修復後 `ffmpeg1pass`
6. 修復後 `ffmediatoolkit`
7. 修復後 `autogen`
8. `opencv`
9. `gave_up`

意図:
- 長尺 `No frames decoded` は `one-pass` の価値が高い
- `autogen` は main で一度落ちているので、修復後入力まで後ろへ寄せる
- `opencv` は別実装の最後の保険に留める

### 5.2 `R-B ultra-short-no-frames`

対象:
- `S-02 ultra-short-no-frames`

順序:
1. `autogen`
2. `ffmpeg1pass`
3. `ffmediatoolkit`
4. `opencv`
5. `gave_up`

意図:
- 資料群が示すとおり、超短尺は `tiny seek` と先頭寄り候補が効きやすい
- これは今の実装では最も `autogen` が近い
- `repair` は既定では入れない

### 5.3 `R-C near-black-or-old-frame`

対象:
- `S-04 near-black-or-old-frame`

順序:
1. `autogen`
2. `ffmpeg1pass`
3. `ffmediatoolkit`
4. `gave_up`

意図:
- これは「無画像」ではなく「採用基準ミス」に近い
- `repair` と `opencv` を常用で混ぜる価値は低い

### 5.4 `R-D corrupt-or-partial`

対象:
- `S-05 corrupt-or-partial`

順序:
1. `ffmpeg1pass`
2. `repair.probe`
3. `repair.remux`
4. 修復後 `ffmpeg1pass`
5. 修復後 `ffmediatoolkit`
6. 修復後 `autogen`
7. `opencv`
8. `gave_up`

意図:
- 破損疑いが強い時は `repair` を前倒しする
- ただし `repair` はこのルートだけで使う

## 6. `opencv` timeout 方針

### 6.1 今回固定する方針

- 通常 engine timeout 既定値は維持する
- `opencv` だけ既定 timeout を長めに分ける
- 既定値は 300 秒とする

### 6.2 ここで解決しないもの

- `opencv` が token を無視して戻らない問題そのもの
- これは nominal timeout を長くしても本質解決にはならない
- よって後段で
  - child process 化
  - watchdog
  - hard kill
  を検討する

### 6.3 なぜ今は長めでよいか

- `opencv` は最後尾なので、本体テンポには影響しない
- `opencv` が効く個体を早すぎる timeout で切り捨てない価値がある
- ただし hard timeout 問題が残るため、最終解ではない

## 7. `FailureDb` 書込アルゴ

### 7.1 原則

- 親行
  - `Lane = normal/slow`
  - 状態の正本
- 子行
  - `Lane = rescue`
  - 失敗試行の append ログ

親行を増やさず、救済過程は子行へ逃がす。

### 7.2 親行で持つもの

- `Status`
- `AttemptGroupId`
- `LeaseOwner`
- `LeaseUntilUtc`
- `OutputThumbPath`
- `ResultSignature`
- 直近の `ExtraJson`

### 7.3 子行で持つもの

- `AttemptNo`
- `Engine`
- `FailureKind`
- `FailureReason`
- `ElapsedMs`
- `SourcePath`
- `RepairApplied`
- `ExtraJson`

### 7.4 書くタイミング

1. lease 取得
   - 親行を `processing_rescue`
   - `AttemptGroupId` 採番
2. ルート選択
   - 親行 `ExtraJson` に `RouteId`, `SymptomClass` を載せる
3. engine 試行開始
   - 親行 `ExtraJson` に `CurrentEngine`, `CurrentPhase`, `AttemptNo` を載せる
4. engine 失敗
   - 子行を `attempt_failed` で append
5. repair probe negative
   - 子行は増やさず、親行を `gave_up`
6. 成功
   - 親行だけ `rescued`
7. 本体反映後
   - 親行を `reflected`

### 7.5 書き方の原則

- 試行開始ごとに子行は増やさない
- 高コスト試行の失敗だけ append する
- 親行は「今どこで止まっているか」を見るための progress snapshot に使う
- 子行は「何を試して何で死んだか」を見るために使う

## 8. 実装方針

### 8.1 今すぐ入れるもの

1. `opencv` 既定 timeout の分離
2. `RouteId` と `SymptomClass` の設計固定
3. 現行 fixed 順を route 切替へ置換する準備

### 8.2 次の実装単位

1. `ClassifyRescueSymptom(...)`
2. `BuildRescuePlan(...)`
3. `UpdateRescueProgressSnapshot(...)`
4. route ごとの engine 列挙

### 8.3 後段

1. `opencv` hard timeout watchdog
2. `repair` 前後の source 切替を子行 `ExtraJson` に統一
3. `near-black` の専用分類キー追加

## 9. タスクリスト

| ID | 状態 | タスク | メモ |
| --- | --- | --- | --- |
| `RES-011` | 完了 | `opencv` 既定 timeout を engine 別に長めへ分ける | nominal timeout 300秒 |
| `RES-012` | 完了 | `ClassifyRescueSymptom(...)` を追加する | 親失敗 reason と軽量情報から route を決める |
| `RES-013` | 完了 | route 別 `BuildRescuePlan(...)` を追加する | fixed 順を route 方式へ置換 |
| `RES-014` | 完了 | 親行 `ExtraJson` の progress snapshot 更新を追加する | `CurrentEngine`, `CurrentPhase`, `RouteId`, `SymptomClass` |
| `RES-015` | 未着手 | `opencv` hard timeout watchdog を実装する | token 非対応対策 |

## 10. 一言まとめ

- 本体はもう `autogen` 単発でよい
- 救済exeは、ここからは「全部試す」ではなく「症状を切って順序を変える」段階である
- `opencv` は最後尾維持、timeout は長め、hard timeout は別タスクで締める
