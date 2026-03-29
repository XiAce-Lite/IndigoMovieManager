# Implementation Plan_サムネ起動導線安定化_2026-03-29

最終更新日: 2026-03-29

変更概要:
- 通常ルートと通常ルート以外を含むサムネ起動導線の実装差分を整理
- 「サムネ作成」「サムネ救済」「単処理サムネ救済」の入口ごとの揺れを調査
- 導線共通化を軸にした安定化計画と実装プランを作成

## 1. 目的

- 右クリック等を含む全導線で、サムネ起動の開始条件と開始結果を揃える。
- route ごとに違う busy 判定、選択解決、再試行方針を減らす。
- `workthree` 本線の方針に合わせ、通常動画の体感テンポを壊さずに安定化する。

## 2. 今回の前提

- 正本は `AI向け_ブランチ方針_workthreeユーザー体感テンポ最優先_2026-03-11.md` と `AI向け_現在の全体プラン_workthree_2026-03-20.md` とする。
- `AI向け_ブランチ方針_future難読動画実験線_2026-03-11.md` は今回の作業ツリー内では確認できなかったため、`workthree` 本線資料を基準に判断する。
- 今回は実装着手前の調査と計画整理を目的とし、まだコード変更は行わない。
- ユーザー要請で起動した処理は、開始結果と受付結果を必ずポップアップで返す。

## 3. ながれ調査

### 3.1 導線一覧

| 導線 | 入口 | 実処理 | 主なファイル |
|---|---|---|---|
| 通常サムネ生成 | 通常一覧や監視からの投入 | `TryEnqueueThumbnailJob()` | `Thumbnail/MainWindow.ThumbnailQueue.cs` |
| 等間隔サムネイル作成 | 右クリック `等間隔サムネイル作成` | `TryEnqueueThumbnailRescueJob()` | `Thumbnail/MainWindow.ThumbnailCreation.cs` |
| 手動キャプチャ差し替え | プレイヤー `Capture` | `CreateThumbAsync()` 直呼び | `Views/Main/MainWindow.Player.cs` |
| 右クリック救済 | 右クリック `サムネイル救済` | `TryEnqueueThumbnailRescueJobDetailed()` | `Views/Main/MainWindow.MenuActions.cs` |
| 右クリック黒背景救済 | 右クリック `簡易黒背景対策` / `徹底黒背景対策` | rescue request | `Views/Main/MainWindow.MenuActions.cs` |
| 右クリックインデックス再構築 | 右クリック `インデックス再構築` | `TryStartThumbnailDirectIndexRepairWorker()` | `Views/Main/MainWindow.MenuActions.cs` |
| 上側 `サムネ救済` タブ 一括通常再試行 | ボタン `一括通常再試行` | `TryEnqueueThumbnailJob(..., bypassTabGate: true)` | `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs` |
| 上側 `サムネ救済` タブ 単処理救済 | 下段の黒背景対策 / 黒確定 / インデックス再構築 | rescue request / 黒jpg直保存 / direct worker | `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs` |
| 下部 `サムネ失敗` タブ 選択救済 / 一括救済 | ボタン | `TryEnqueueThumbnailDisplayErrorRescueJob()` | `Watcher/MainWindow.ThumbnailFailedTab.cs` |
| 上側タブの ERROR 画像自動救済 | タブ表示時の可視範囲検出 | 初回のみ通常キュー優先、以後 rescue | `BottomTabs/Common/MainWindow.BottomTabs.Common.cs` / `Thumbnail/MainWindow.ThumbnailRescueLane.cs` |
| 詳細タブの欠損 / ERROR 補正 | 詳細サムネ表示時 | 通常キュー or rescue | `BottomTabs/Extension/MainWindow.BottomTab.Extension.DetailThumbnail.cs` |

### 3.2 主要な分岐

#### A. 通常キュー

- `TryEnqueueThumbnailJob()` は上側通常タブ `0..4` だけを正式対象にする。
- `ExtensionDetailThumbnailTabIndex=99` だけは例外で通す。
- `bypassTabGate` が無い通常導線は、現在表示中の上側タブと一致しないと投入されない。
- preferred 優先度は debounce で落ちない。

#### B. 救済 request

- `TryEnqueueThumbnailRescueJobDetailed()` は FailureDb へ `pending_rescue` を積み、必要なら外部 RescueWorker 起動を試みる。
- `skipWhenSuccessExists` が true の導線は、正常 jpg が既にあると要求を落とす。
- `useDedicatedManualWorkerSlot` が true の導線だけ manual slot を使う。
- `requiresIdle` と priority により、起動を待機させるか即時にするかが変わる。

#### C. direct worker

- `TryStartThumbnailDirectIndexRepairWorker()` は FailureDb を経由しない。
- manual slot が埋まっていると、その場で開始失敗になる。
- 後続で自動再評価する保険は持っていない。

### 3.3 導線ごとの差分

| 観点 | 通常キュー | rescue request | direct worker |
|---|---|---|---|
| 重複吸収 | debounce + QueueDb 一意制約 | FailureDb の open request 判定 | slot 空きだけ |
| 対象解決 | 現在の上側タブ選択 | route ごとの選択解決 | route ごとの選択解決 |
| busy 時 | QueueDb に積めれば後で処理 | request 受理後も worker 開始は揺れる | その場で失敗 |
| 可視フィードバック | 進捗タブ中心 | 進捗 + FailureDb | 手動進捗ポップアップ中心 |
| 既存成功 jpg | そのまま再生成可能 | route により skip あり | 対象外 |

## 4. 不安定化の主因

### 4.1 起動契約が 3 系統に割れている

- 「作成」「救済」「単処理救済」が見た目上は近いのに、内部では通常キュー / rescue request / direct worker に分かれている。
- そのため、同じ操作感を期待しても busy 時の動き、重複時の動き、既存成功 jpg の扱いが一致していない。

### 4.2 下部 `サムネ失敗` タブが共通右クリックメニューに対して文脈を渡せていない

- 下部 `ThumbnailErrorTabView` も `menuContext` を使う。
- しかし右クリックハンドラ側は `GetCurrentUpperTabFixedIndex()` と `GetSelectedItemsByTabIndex()` を基準にしている。
- このため下部 `サムネ失敗` タブ上で右クリックしても、対象解決は上側タブの選択へ流れる。
- 上側 `サムネ救済` タブは専用分岐で救われているが、下部 `サムネ失敗` タブは救われていない。

### 4.3 manual slot busy 時の扱いが route ごとに違う

- rescue request は FailureDb へ記録されるが、manual slot 起動が失敗した時の再評価は弱い。
- `ScheduleDelayedThumbnailRescueWorkerLaunch()` は初回 `launchRequested=true` の時だけ走るため、manual slot busy で最初から `false` だった要求は「受理はされたが即時開始されない」状態になりやすい。
- direct index repair は FailureDb を通らないため、busy なら開始失敗で終わる。

### 4.4 `サムネ作成` の見た目と実 route が一致していない

- `等間隔サムネイル作成` は通常キューではなく rescue request へ流している。
- 当初 `manual-equal-interval` は `skipWhenSuccessExists=true` だったが、ユーザー要請なら既存成功 jpg があっても再作成を通す方針へ変更した。
- これで `等間隔サムネ作成` と右クリック `サムネイル救済` の「既存成功 jpg があっても作り直したい」意図を揃えた。

### 4.5 自動 placeholder 救済も route が一定ではない

- `tab-error-placeholder` 初回だけ通常キューへ戻し、履歴ありでは rescue に切り替える。
- 一方 `detail-error-placeholder` は最初から rescue request に送る。
- 「ERROR からの救済」という見た目が同じでも、導線ごとに開始 route が違う。

## 5. 安定化計画

### Phase 1. 導線の入口契約を揃える

- `ThumbnailActionContext` のような中立 DTO を追加し、入口ごとの差分を「対象」「意図」「希望 route」「busy 時方針」で表現する。
- 右クリック、上側救済タブ、下部失敗タブ、詳細タブからの要求生成をここへ寄せる。
- `GetSelectedItemsByTabIndex()` 直呼びではなく、発火元 control ごとの選択解決を明示する。

### Phase 2. 開始判定を一か所へ寄せる

- `TryEnqueueThumbnailJob()` / `TryEnqueueThumbnailRescueJobDetailed()` / `TryStartThumbnailDirectIndexRepairWorker()` の直呼びを薄くし、開始判定を `ThumbnailActionDispatcher` 相当へ寄せる。
- `skipWhenSuccessExists`、`requiresIdle`、`useDedicatedManualWorkerSlot` を route 固有 if ではなく、意図ベースで決める。
- 同じ「明示再作成」は既存成功 jpg の扱いを揃える。

### Phase 3. manual slot busy の再評価を強化する

- manual rescue request は、初回起動失敗でも delayed recheck を掛けられるようにする。
- direct index repair は開始結果を `Started / Busy / Unsupported` で返し、UI 側の反応を固定する。
- 直起動を維持するなら「busy で不開始」を明確通知し、誤って受理済みに見せない。

### Phase 4. 文脈のズレる右クリックを止める

- 下部 `サムネ失敗` タブ専用の選択解決を追加する。
- 対応完了までの暫定策としては、下部 `サムネ失敗` タブで共通右クリックメニューを無効化する案もある。
- ただし本線では「無効化で逃がす」より、対象文脈を正しく渡して通常ルートへ合流させる方を優先する。

### Phase 5. 観測とテストを補う

- route ごとに `requested / accepted / deferred / busy / skipped_existing_success / rerouted` を同じ粒度でログへ残す。
- ユーザー要請起点では、結果をトーストではなくポップアップで返す共通ルールを追加する。
- 下記の回帰テストを追加する。
  - 下部 `サムネ失敗` タブ右クリックが上側選択を誤参照しないこと
  - manual slot busy でも rescue request が孤立しないこと
  - 等間隔サムネイル作成と右クリック救済で既存成功 jpg の扱いが設計通りであること
  - `tab-error-placeholder` と `detail-error-placeholder` の route 違いが意図通り固定されること
  - ユーザー要請起点の各 route で、成功 / busy / 対象外 / 既存成功ありをポップアップで返すこと

## 6. 実装プラン

### 6.0 タスクリスト

- [x] Task 1: 下部 `サムネ失敗` タブ右クリック時の対象解決を、上側タブ選択から切り離す
- [x] Task 2: 右クリック系の `サムネ作成` / `サムネ救済` / `インデックス再構築` に、ユーザー要請ポップアップ応答を入れる
- [x] Task 3: manual slot busy 時の結果型と案内文言を揃える
- [x] Task 4: 上側 `サムネ救済` タブと下部 `サムネ失敗` タブのユーザー要請応答を共通化する
- [x] Task 5: route 共通の dispatcher / DTO へ薄く寄せる
- [x] Task 6: 回帰テストとログ粒度を補強する

## 6.0.1 実装進捗メモ（2026-03-29 追記）

- Task 3 までで、manual slot busy を `Started / Busy / Invalid` で扱えるようにし、ユーザー向けメッセージは共通 builder へ寄せた。
- Task 4 までで、右クリック系に加えて上側 `サムネ救済` タブと下部 `サムネ失敗` タブのユーザー要請も、必ずポップアップで返す形へ揃えた。
- Task 5 までで、`等間隔サムネ作成` / 右クリック救済 / 右クリックインデックス再構築 / 上側 `サムネ救済` タブのインデックス再構築を、薄い dispatcher / DTO へ寄せた。
- Task 6 までで、件数集計 helper の回帰テストと、上側 `サムネ救済` タブ / 下部 `サムネ失敗` タブの完了ログ粒度を補強した。
- ユーザー要請の OK のみ結果通知は、modal ダイアログではなく長め表示の overlay msg へ寄せる方針に更新した。
- 今回スコープでは Task 1〜6 を完了とする。さらに大きい抽象化は、体感テンポと可読性を見ながら別タスクで判断する。

### 6.1 変更順

1. 対象選択の共通化
   - `BottomTabs/Common/MainWindow.BottomTabs.Common.cs`
   - `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs`
   - `Watcher/MainWindow.ThumbnailFailedTab.cs`
   - まず「どの一覧から来た要求か」を誤らない状態にする。

2. 起動要求 DTO と dispatcher 追加
   - `Thumbnail` 配下に route 中立の要求 DTO と dispatcher を置く。
   - 既存メソッドは薄い adapter にして、判断は dispatcher へ寄せる。

3. manual rescue / direct index repair の結果型整理
   - `bool` 戻り値を段階的に `enum` 戻り値へ寄せる。
   - UI は `Started / Deferred / Busy / Skipped / Invalid` を見て、ユーザー要請起点なら必ずポップアップで案内を返す。

4. 下部 `サムネ失敗` タブ右クリックの是正
   - 共通 menu からでも error tab selection を正しく解決できるようにする。
   - 導線共通化前に局所 if を増やしすぎない。

5. route テスト追加
   - 既存の `MissingThumbnailRescuePolicyTests` と launcher test 群を拡張する。
   - 新しい dispatcher の判定テストを追加する。

### 6.2 実装時の具体ルール

- hot path の通常キューは重くしない。DTO 生成だけで終わる形を維持する。
- `Factory + Interface + Args` の境界は崩さない。今回の対象は UI 入口と rescue 入口の整理であり、`ThumbnailCreationService` 本体へ責務を戻さない。
- route の違いを `if (reason == "...")` で増やさず、意図 enum へ寄せる。
- 右クリックや下部タブ専用の UI 文脈は、`sender` / `ContextMenu.PlacementTarget` から辿れる範囲で解決し、上側タブ全体の状態へ暗黙依存しない。
- ユーザー要請で発火した処理は、受付成功・開始待ち・空き不足・対象外・既存成功ありのいずれでも必ずポップアップで返す。
- 自動救済や可視範囲救済のような非ユーザー要請経路には、このポップアップ義務を持ち込まない。

### 6.3 完了条件

- 通常一覧、右クリック、上側 `サムネ救済` タブ、下部 `サムネ失敗` タブで「対象解決」と「開始結果」が説明できる。
- manual slot busy 時に、受理済みなのに始まらない曖昧状態を減らせている。
- `等間隔サムネイル作成` と `サムネイル救済` の既存成功 jpg ポリシー差分を、意図した差分として説明できる。
- 下部 `サムネ失敗` タブ右クリックが上側選択へ誤爆しない。
- ユーザー要請起点の導線では、結果が必ずポップアップで返る。

## 7. 先にやらないこと

- RescueWorker 本体の全面再設計
- FailureDb スキーマ変更
- `ThumbnailCreationService` 本体への責務戻し
- 難読動画条件の追加拡張
- UI デザインの大改修

## 8. いまの結論

- 不安定さの中心は、個別 route の局所バグより「導線が別々に育って開始契約が揺れている」点にある。
- 最初の一手は rescue ロジック追加ではなく、入口の選択解決と開始判定の共通化である。
- 特に下部 `サムネ失敗` タブの右クリック文脈と、manual slot busy 時の再評価不足は優先度が高い。
