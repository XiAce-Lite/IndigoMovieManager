# AI向け レビュー指示 Claude T9c WatchFolderDrop正規化復元 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `WatchFolderDropRegistrationPolicy` の正規化と重複判定契約が戻っているかを判定する

## 2. レビュー対象

- `Watcher/WatchFolderDropRegistrationPolicy.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatchFolderDropRegistrationPolicyTests.cs`

## 3. 必ず見る観点

- `C:\temp` と `C:\temp\` を同一として扱えているか
- `CanAccept` が受理可能時に不要な列挙を続けていないか
- 既存登録重複 / 新規登録 / 無効入力の分岐がテストで固定されているか
- `Build(...)` 経由へ寄せたことで、従来より処理幅が広がっていないか

## 4. 今回レビューしないもの

- `RenameBridge`
- `WatchScanCoordinator`
- `WatcherRegistrationDirectPipelineTests`
- `EventQueue`

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
