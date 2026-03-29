# AI向け 作業指示 Codex T9b WatcherRenameBridge安全契約復元 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `Watcher RenameBridge` の安全契約だけを復元する
- `Watcher residual` の混在帯から、`RenameBridge` 単独で受け入れ可能な最小差分を切り出す

## 2. 対象ファイル

- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`

## 3. 今回の必達

- stale watch scope を通った rename が、別 DB / 別 UI 状態へ流れない
- snapshot / guard / rollback 前提を戻す
- `Movie_Path` / `Movie_Name` だけでなく、rename 後の movie state 契約を壊さない
- hash jpg / `.#ERROR.jpg` / bookmark jpg / bookmark DB rename が、別 owner や部分一致を巻き込まない
- rollback は「実施済み段だけ戻す」をテストで固定する

## 4. 禁止

- `Watcher/MainWindow.WatchScanCoordinator.cs` を触らない
- `Watcher/WatchFolderDropRegistrationPolicy.cs` を触らない
- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs` を触らない
- `EventQueue` や `Created direct pipeline` の設計変更を混ぜない
- broad `catch (Exception) { }` で握り潰さない

## 5. レビューで指摘済みの論点

- `MainVM` の現在値をその場で直接読む形へ戻っている
- `Movie_Body` / `Ext` / `Drive` / `Dir` の追従が落ちている
- hash jpg / `.#ERROR.jpg` / bookmark jpg の共有資産を誤 rename し得る
- bookmark DB rename が `replace(...) where like '%oldName%'` へ戻っており危険
- `WatcherRenameBridgePolicyTests.cs` が消えており、契約固定が失われている

## 6. 受け入れ条件

- `RenameBridge` だけでレビュー専任役が `findings なし` と判定する
- 対象外ファイルへ差分が広がらない
- 追加 / 復元したテストが、共有資産ガード、rollback、stale guard を固定している

## 7. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 復元テスト一覧
- 実行した確認コマンドと結果
- 残リスク
