# 調査結果 master vs workthree 保存場所と内容の比較 2026-03-27

## 目的
- `IndigoMovieManager-master` と `IndigoMovieManager_fork_workthree` を別フォルダで実行する前提で、
  保存場所と保存内容の差分を後から迷わず確認できるようにする。
- どこが安全に分離されていて、どこはまだ運用注意が要るかを整理する。

## 前提
- 比較対象:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager-master\IndigoMovieManager-master`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree`
- 実行前提:
  - 両者は別フォルダから起動する。
  - 同じ `layout.xml` と同じ `Thumb` / `bookmark` フォルダを共有しない。
  - 同じ `.wb` DB を同時に開いて更新しない。
- 本書は 2026-03-27 時点のコードと、この環境の `%LOCALAPPDATA%` 観測結果をまとめたもの。

## 結論
- 別フォルダ実行なら、`layout.xml`、既定の `Thumb`、既定の `bookmark` は分離しやすい。
- `workthree` のログ系 / QueueDb 系 / FailureDb 系 / RescueWorkerSessions は
  `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree` に固定されており、`master` と分離される。
- ただし `user.config` は exe 名の影響を受けるため、
  `workthree` 側は `IndigoMovieManager_fork_workthree.exe` を使う前提で運用した方が安全。
- 一番危ないのは保存先の衝突よりも、同じ `.wb` を両方で同時更新する運用である。

## 比較表
| 項目 | master | workthree | 保存内容 | 別フォルダ実行での評価 |
| --- | --- | --- | --- | --- |
| アプリ共通設定 `user.config` | `%LOCALAPPDATA%\IndigoMovieManager\IndigoMovieManager_Url_xxx\1.0.0.0\user.config` | exe 名ベースの `user.config`。`IndigoMovieManager_fork_workthree.exe` で起動する運用が安全 | 画面位置、直近 DB、最近使った DB、共通設定 | 条件付きで分離可 |
| `layout.xml` | 実行フォルダ直下 | 実行フォルダ直下 | Dock 配置 | 別フォルダなら分離可 |
| 既定サムネイル保存先 | 実行フォルダ配下 `Thumb\<DB名>\...` | 基本は実行フォルダ配下 `Thumb\<DB名>\...`。ただし WhiteBrowser 同居 DB は `DBフォルダ\thum\<DB名>` 優先 | サムネイル画像 | 別フォルダなら分離可 |
| bookmark 保存先 | DB の `system.bookmark`。未設定時の既定は実行フォルダ配下 `bookmark\<DB名>` を使う箇所あり | DB の `system.bookmark` | bookmark 画像 | DB 設定次第 |
| `LastDoc` / `RecentFiles` | `user.config` | `user.config` | 最近開いた `.wb` 情報 | 条件付きで分離可 |
| `sort` / `skin` / `thum` / `bookmark` など DB 設定 | 開いた `.wb` の `system` テーブル | 開いた `.wb` の `system` テーブル | DB 個別設定 | 同じ `.wb` 同時利用は非推奨 |
| ログ | コード根拠は薄いが、この環境では `%LOCALAPPDATA%\IndigoMovieManager\logs` が存在 | `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs` 固定 | 実行ログ、サムネ関連ログ | workthree は分離済み |
| QueueDb | この環境では `%LOCALAPPDATA%\IndigoMovieManager\QueueDb` が存在 | `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\QueueDb` 固定 | サムネキュー DB | workthree は分離済み |
| FailureDb | 観測できず | `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\FailureDb` 固定 | 失敗系 DB | workthree は分離済み |
| RescueWorkerSessions | 観測できず | `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\RescueWorkerSessions` 固定 | rescue worker セッション | workthree は分離済み |

## 保存場所ごとの詳細

### 1. `user.config`
- 両者とも `ApplicationSettingsBase` を使っている。
  - `master`: `Properties\Settings.Designer.cs`
  - `workthree`: `Properties\Settings.Designer.cs`
- `master` の設定定義は `App.config` にあり、主に次を持つ。
  - `MainLocation`
  - `MainSize`
  - `LastDoc`
  - `RecentFiles`
  - `AutoOpen`
  - `ConfirmExit`
  - `DefaultPlayerPath`
  - `DefaultPlayerParam`
  - `RecentFilesCount`
  - `IsResizeThumb`
  - `CheckExt`
- `workthree` は上記に加えて次を持つ。
  - `DeleteKeyActionMode`
  - `ShiftDeleteKeyActionMode`
  - `CtrlDeleteKeyActionMode`
  - `ThumbnailPriorityLaneMaxMb`
  - `ThumbnailSlowLaneMinGb`
  - `ThumbnailParallelism`
  - `EverythingIntegrationEnabled`
  - `EverythingIntegrationMode`
  - `FileIndexProvider`
- この環境では `master` の `user.config` 実体を
  `%LOCALAPPDATA%\IndigoMovieManager\IndigoMovieManager_Url_xxx\1.0.0.0\user.config`
  で確認できた。
- 一方で `workthree` は LocalAppData の独自ルートを持つが、`user.config` 自体は exe 名に依存する。
- そのため、`workthree` は `IndigoMovieManager_fork_workthree.exe` で起動する運用に寄せる方が安全。

### 2. `layout.xml`
- `master` は `layout.xml` をカレントディレクトリから読み書きする。
- `workthree` も `DockLayoutFileName = "layout.xml"` として実行フォルダ直下へ読み書きする。
- よって別フォルダ実行なら衝突しにくい。
- 逆に同じフォルダから両方を起動すると、`layout.xml` は衝突する。

### 3. サムネイル保存先
- `master` の既定サムネイル保存先は実行フォルダ基準で、
  `Thumb\<DB名>\...` を使う。
- `workthree` は `ThumbRootResolver` に寄せてあり、
  優先順位は次の通り。
  1. DB の `system.thum` に明示設定があればそれを使う
  2. DB フォルダに `WhiteBrowser.exe` がある場合は `DBフォルダ\thum\<DB名>` を使う
  3. それ以外は `AppContext.BaseDirectory\Thumb\<DB名>` を使う
- つまり、別フォルダ実行だけでなく、DB の置き場所や `system.thum` の値でも保存先が変わる。

### 4. bookmark 保存先
- 両者とも DB の `system.bookmark` から取得する。
- `master` には実行フォルダ基準の `bookmark\<DB名>` を既定扱いするコードが残っている。
- `workthree` は `system.bookmark` を UI へ読み戻す構成で、
  既定パスを固定で補完するコードよりも DB 側設定依存が強い。
- よって bookmark は「アプリ間で分離されるか」よりも
  「その `.wb` の `system.bookmark` がどこを向いているか」で決まる。

### 5. MainDB (`.wb`)
- 両者とも `LastDoc` を `user.config` に保存し、起動時の自動オープンに使う。
- 両者とも開いた DB の `system` テーブルを読み、
  `sort` / `skin` / `thum` / `bookmark` などを参照する。
- 両者とも操作中に `UpsertSystemTable(...)` で `system` テーブルを書き換える。
- そのため、同じ `.wb` を両方で同時起動すると、
  SQLite ロックや設定上書きの意味で安全ではない。

### 6. `workthree` の LocalAppData 固定保存先
- `workthree` は `AppLocalDataPaths` でルート名を
  `IndigoMovieManager_fork_workthree` に固定している。
- 保存先は次の通り。
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs`
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\QueueDb`
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\FailureDb`
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\RescueWorkerSessions`
- ここは `master` と分離しやすい。

## コード根拠

### master
- `App.config`
- `MainWindow.xaml.cs`
- `Settings.cs`
- `SQLite.cs`
- `TabInfo.cs`

### workthree
- `App.config`
- `IndigoMovieManager.csproj`
- `Views\Main\MainWindow.xaml.cs`
- `Views\Main\MainWindow.MenuActions.cs`
- `DB\DbSettings.cs`
- `DB\SQLite.cs`
- `Thumbnail\MainWindow.ThumbnailPaths.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbRootResolver.cs`
- `src\IndigoMovieManager.Thumbnail.Runtime\AppLocalDataPaths.cs`

## いまの運用ルール
1. `master` と `workthree` は必ず別フォルダから起動する。
2. `workthree` は可能な限り `IndigoMovieManager_fork_workthree.exe` から起動する。
3. 同じ `.wb` を 2 プロセスで同時に開かない。
4. bookmark / thum を明示設定している DB は、その `system` テーブル値も確認してから使う。

## 補足
- 過去ドキュメントには `workthree` の `AssemblyName / Product / Company` を
  分離済みと書かれているものがあるが、2026-03-27 時点の
  `IndigoMovieManager_fork_workthree\IndigoMovieManager.csproj` では
  `AssemblyName` / `Product` / `Company` は `IndigoMovieManager` になっている。
- 今回の比較は、過去メモではなく現在のワークツリー内容を優先した。
