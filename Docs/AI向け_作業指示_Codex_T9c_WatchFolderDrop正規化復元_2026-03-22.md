# AI向け 作業指示 Codex T9c WatchFolderDrop正規化復元 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `WatchFolderDropRegistrationPolicy` の末尾セパレータ正規化と重複判定契約を戻す
- `CanAccept` の早期 return 契約を戻す

## 2. 対象ファイル

- `Watcher/WatchFolderDropRegistrationPolicy.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatchFolderDropRegistrationPolicyTests.cs`

## 3. 必達

- `C:\temp` と `C:\temp\` を同一フォルダとして扱う
- 既存登録と drop 候補の重複判定が、末尾セパレータ差で崩れない
- `CanAccept` は受理可能判定が出た時点で不要な後続列挙を続けない
- 重複 / 新規 / 無効入力の既存契約をテストで固定する

## 4. 禁止

- `Watcher/MainWindow.WatcherRenameBridge.cs` を触らない
- `Watcher/MainWindow.WatchScanCoordinator.cs` を触らない
- `Watcher/MainWindow.WatcherEventQueue.cs` を触らない
- flowchart や docs 更新をこのレーンへ混ぜない

## 5. 受け入れ条件

- `WatchFolderDrop` だけでレビュー専任役が `findings なし`
- 対象外ファイルへ差分が広がらない
- 末尾セパレータ正規化と `CanAccept` 早期 return がテストで見える

## 6. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 修正テスト一覧
- 実行した確認コマンドと結果
- 残リスク
