# Implementation Plan（Everything専用監視モード導入 2026-03-05）

## 0. 背景

- 現状は以下の2経路が同時に動く。
  - `FileSystemWatcher` (`CreateWatcher` -> `RunWatcher`)
  - Everythingポーリング (`RunEverythingWatchPollLoopAsync` -> `QueueCheckFolderAsync`)
- この二重運用は安全側だが、環境次第で重複検知・無駄なUI更新を増やす。

## 1. 目的

- Everything利用可能な環境では、監視をEverything側へ寄せる。
- `FileSystemWatcher` とEverythingポーリングの二重実行を減らし、UI詰まり要因を削る。

## 2. 方針

- `EverythingIntegrationMode = On` を「Everything優先監視モード」として扱う。
- `On` かつ Everything可用 かつ パスがeligible（ローカル固定 + NTFS）の場合:
  - `CreateWatcher` で `FileSystemWatcher` を張らない。
- それ以外（Auto/Off、または不可用・非eligible）は既存どおり `FileSystemWatcher` を維持する。
- `On` モードでは、Everythingが一時不可用でもポーリングは継続し、`CheckFolderAsync` 側のfilesystem fallbackで監視を継続する。

## 3. スコープ

- IN
  - `Watcher/MainWindow.Watcher.cs`: watcher生成条件の追加
  - `MainWindow.xaml.cs`: Onモード時のポーリング継続条件の調整
  - ログ追加（skip理由を追跡可能にする）
- OUT
  - 設定UI文言・選択肢の追加変更
  - Everything providerの実装差し替え
  - MainDBスキーマ変更

## 4. タスクリスト

| ID | 状態 | タスク | 対象 |
|---|---|---|---|
| EOM-001 | 完了 | `CreateWatcher` にEverything専用スキップ条件を追加 | `Watcher/MainWindow.Watcher.cs` |
| EOM-002 | 完了 | `ShouldRunEverythingWatchPoll` をOnモード時は不可用でも継続するよう調整 | `MainWindow.xaml.cs` |
| EOM-003 | 完了 | 診断ログ追加（watcher skip理由、fallback継続理由） | `Watcher/MainWindow.Watcher.cs` `MainWindow.xaml.cs` |
| EOM-004 | 完了 | ビルド/テスト確認 | `MSBuild` `dotnet test` |

## 5. 受け入れ基準

- Onモード + eligibleフォルダ + Everything可用時:
  - `CreateWatcher` のログに watcher skip が出る。
  - Everythingポーリングで検知・登録・サムネキュー投入が継続する。
- OnモードでEverything停止時:
  - ポーリングが止まらず、`CheckFolderAsync` 側filesystem fallbackで監視継続する。
- Auto/Offモード:
  - 既存動作（watcher常駐）が変わらない。
