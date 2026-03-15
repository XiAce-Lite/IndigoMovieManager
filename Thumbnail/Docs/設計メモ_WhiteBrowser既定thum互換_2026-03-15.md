# 設計メモ: WhiteBrowser既定 `thum` 互換（2026-03-15）

最終更新日: 2026-03-15

## 目的

- `system.thum` 未設定の `.wb` を本exeで開いた時でも、WhiteBrowser既定配置とサムネ保存先がズレにくいようにする。
- `C:\WhiteBrowser\maimai.wb` のように WhiteBrowser.exe と同居するDBでは、`C:\WhiteBrowser\thum\maimai` を既定根として扱う。
- それ以外のDBでは、従来どおり本exe側の `AppContext.BaseDirectory\Thumb\<DB名>` を維持する。

## 今回固定した解決順

1. `system.thum` が入っていれば、その値を最優先で使う。
2. `system.thum` が空で、DBと同じフォルダに `WhiteBrowser.exe` が存在すれば、
   `dbDir\thum\<DB名>` を既定根として使う。
3. 上記に当てはまらなければ、従来どおり `AppContext.BaseDirectory\Thumb\<DB名>` を使う。

## 変更箇所

- `Thumbnail/TabInfo.cs`
  - 実運用向けのサムネ根解決 `ResolveRuntimeThumbRoot(...)` を追加。
- `MainWindow.xaml.cs`
  - `GetSystemTable(...)` で `system.thum` 未設定時も runtime 上は既定根を解決して保持するように変更。
  - リネーム時のサムネ探索も同じ解決順へ統一。
- `MainWindow.MenuActions.cs`
  - サムネ削除時の探索も同じ解決順へ統一。
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
  - worker 側の出力先解決でも同じ互換判定を使うように変更。

## テスト

- `Tests/IndigoMovieManager_fork.Tests/TabInfoTests.cs`
  - 明示設定優先
  - WhiteBrowser同居DBの `thum` 既定化
  - 通常DBの従来 fallback 維持
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailRescueWorkerLauncherTests.cs`
  - worker 側でも WhiteBrowser同居DBが `thum` 配下へ解決されることを追加確認

## 注意

- `C:\WhiteBrowser\` を絶対パス固定する実装にはしていない。
- WhiteBrowser.exe 同居判定にしたので、既定インストール先以外の WhiteBrowser 配置でも互換動作しやすい。
- 既に `C:\WhiteBrowser\thum\...` に残っている error サムネ自体は、この修正だけでは正常画像へ置き換わらない。
  必要なら再生成が別途必要。
