# UpperTabs Big10

このフォルダは、上側 `5x2` (`Big10`) タブの個別 dir 化を進めるための入口です。

## 今回の段階

- `MainWindow.UpperTab.Big10.cs` を追加
- `5x2` タブを明示選択する入口
- `BigList10` 取得と先頭選択

## 補足

- 既存互換の都合で、`5x2` は既定スキンの保存先には使わない
- そのため今回は `SwitchTab` には触れず、helper 入口だけ先に用意する

## 次に寄せる候補

- `5x2` タブ固有の選択同期
- `5x2` タブ固有の viewport / visible-first 補助
- 将来の `Big10TabView` host 化
