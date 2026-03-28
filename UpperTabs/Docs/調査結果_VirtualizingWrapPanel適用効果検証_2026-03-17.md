# 調査結果: VirtualizingWrapPanel 適用効果検証 (2026-03-17)

最終更新日: 2026-03-17

## 1. 結論

- 上側タブ (`Small / Big / Grid / Big10`) では、`VirtualizingWrapPanel` はすでに導入済み。
- そのため、「これから上側タブへ VirtualizingWrapPanel を入れると急に滑らかになるか」という問いに対する答えは **No**。
- ただし、`VirtualizingWrapPanel` 自体は効いている。可視範囲ログを見る限り、数千件表示でも実際に見えている数十件だけを相手にできている。
- 現在の詰まりの本命は、仮想化不足そのものよりも、
  - `FilteredMovieRecs` 更新時の `Reset`
  - 1件テンプレートの重さ
  - 画像キャッシュの小ささ
  に移っている。

## 2. 確認した事実

### 2.1 上側タブ本体はすでに導入済み

- `Views/Main/MainWindow.xaml`
  - `SmallList`
  - `BigList`
  - `GridList`
  - `BigList10`

上記 4 タブは、いずれも `ListView.ItemsPanel` に `vwp:VirtualizingWrapPanel` を使っている。

### 2.2 可視範囲ログ上でも仮想化は効いている

`%LOCALAPPDATA%\\IndigoMovieManager_fork_workthree\\logs\\debug-runtime.log` より:

- `2026-03-15 17:33:07.363 [ui-tempo] upper tab viewport: tab=2 reason=throttled visible=0-27 near=0-35`
- `2026-03-15 17:34:43.825 [ui-tempo] upper tab viewport: tab=2 reason=throttled visible=0-34 near=0-35`

これは、上側タブの可視追跡が「数十件」単位で動いていることを示す。
少なくとも「数千件を全部 UI 要素化している」状態ではない。

### 2.3 今の重さは別の所が支配している

同じログより:

- `2026-03-17 00:27:34.996 [ui-tempo] filter end: ... count=2165 ... source_apply_ms=10011 ... total_ms=10605`

このケースでは、重いのは一覧描画前段の `source_apply_ms` であり、
`VirtualizingWrapPanel` の有無だけでは片付かない。

### 2.4 VirtualizingWrapPanel タブは更新方式を安全側へ倒している

- `UpperTabs/Common/UpperTabCollectionUpdatePolicy.cs`
  - `List` タブ以外は `FilteredMovieRecsUpdateMode.Reset`
- `ViewModels/MainWindowViewModel.cs`
  - `ResetFilteredMovieRecs(...)` は `Clear() + Add(...)`

既存実装では、`VirtualizingWrapPanel` と差分更新の相性問題を避けるため、
`Small / Big / Grid / Big10` は `Reset` 固定になっている。
つまり、仮想化は効いていても、検索や並び替えでは一覧全体の差し替えコストが残る。

### 2.5 1件テンプレートの内側はまだ軽くない

- `Views/Main/MainWindow.xaml`
  - `Small` タブのタグ領域は `ItemsControl + WrapPanel`
  - `List` タブのタグ領域も `ItemsControl + WrapPanel`
- `UserControls/BigDetailControl.xaml`
  - タグ領域は `ItemsControl + WrapPanel`

外側一覧は仮想化されているが、実体化された各アイテムの中には
非仮想のタグ表示が残っている。
ただし、ここは「各動画のタグ数」が比較的少ないなら、主犯にはなりにくい。

### 2.6 画像キャッシュはまだ小さい

- `Infrastructure/Converter/NoLockImageConverter.cs`
  - `MaxCacheEntries = 256`

一覧のスクロールやタブ切替で再 decode が増える余地がある。
ここは `VirtualizingWrapPanel` とは別軸の体感要因。

## 3. 判定

### 3.1 上側タブ本体について

- `VirtualizingWrapPanel` は **導入済みで効果あり**
- ただし **追加導入による大きな改善余地はもう小さい**

### 3.2 タグ領域について

- `WrapPanel` を `VirtualizingWrapPanel` に変える案はある
- ただし効果は限定的な可能性が高い

理由:

- 外側一覧の仮想化が先に効いている
- タグ数は動画件数ほど多くない
- 内側まで仮想化すると、レイアウト不安定化の割に回収量が小さい可能性がある

## 4. 今やる価値が高い順

1. `VirtualizingWrapPanel` タブでの `Reset` 頻度をこれ以上増やさない
2. `Small / Big / Big10` の 1件テンプレートを軽くする
3. `NoLockImageConverter` のキャッシュ戦略を見直す
4. タグ領域の仮想化は、最後に実測付きで判断する

## 5. 次の一手

本当に A/B で確認するなら、次の形が安全:

1. `Grid` タブだけを対象に、`WrapPanel` と `VirtualizingWrapPanel` を切り替えるデバッグスイッチを作る
2. `ui-tempo` ログへ、タブ切替後の最初の可視サムネ出現時間を追加する
3. 516件 / 2165件の 2 ケースで比較する

この形なら、既存の上側タブ本線を壊さずに「まだ効くのか」を数値で判定できる。
