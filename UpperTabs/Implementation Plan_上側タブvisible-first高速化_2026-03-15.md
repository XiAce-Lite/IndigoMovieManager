# Implementation Plan: 上側タブ visible-first 高速化 (2026-03-15)

## 1. 目的

- 上側タブ (`Small / Big / Grid / List / Big10`) の体感速度を上げる。
- 表示中の領域を最優先で描画する。
- 表示中の領域のサムネイル作成を最優先で進める。
- 1 万件規模でもスクロールを引っ掛けにくくする。
- 既存の操作性を壊さず、段階的に導入する。

## 2. 今回の前提

- `ListView` は当面維持する。
- 「共通描画クラス」は作らない。
- 導入するのは「共通描画基盤」。
- まず効く順に切り、最後に必要なら host 化と分割へ進む。

## 3. 非目標

- この Phase で `ListView` を全廃しない。
- この Phase で上側タブの XAML 全面分離まではやらない。
- この Phase で完全 custom viewport 実装には進まない。

## 4. 成功条件

- 非アクティブな上側タブは描画更新しない。
- アクティブタブでは visible 範囲が最優先で更新される。
- サムネイル作成も visible 範囲が最優先で処理される。
- 上側タブの画像 decode が tab ごとの表示サイズに寄る。
- スクロール中に見えていない項目のための仕事で詰まりにくくなる。

## 5. フェーズ構成

### Phase 0: 計測の足場を整える

目的:
- 体感改善を感覚だけでなくログでも見えるようにする。

実施内容:
- 上側タブ切替、スクロール、初回可視サムネ表示のログ点を追加する。
- visible 範囲の先頭/末尾 index を後で記録できる受け皿を作る。
- tab ごとの画像 decode サイズと cache hit を計測できるようにする。

対象候補:
- `MainWindow.xaml.cs`
- `Converter/NoLockImageConverter.cs`

完了条件:
- スクロールと画像読込の前後で、比較できる最小ログが取れる。

### Phase 1: 軽量化の即効薬を先に入れる

目的:
- 可視範囲追跡より先に、無駄な画像負荷を落とす。

実施内容:
- `Small / Big / Grid / Big10 / List` ごとに `DecodePixelHeight` を定義する。
- `NoLockImageConverter` へ `IsExists` だけでなく decode profile を渡す。
- 非アクティブな上側タブの更新を止め、dirty のみ保持する。
- 上側タブ用の共通活性判定 `UpperTabActivationGate` を作る。

新規候補:
- `UpperTabs/Common/UpperTabActivationGate.cs`
- `UpperTabs/Common/UpperTabDecodeProfile.cs`

対象候補:
- `MainWindow.xaml`
- `Converter/NoLockImageConverter.cs`

完了条件:
- 非アクティブ上側タブは更新しない。
- 上側タブの画像読込が tab 別 decode サイズに寄る。

### Phase 2: 可視範囲を取る

目的:
- active tab の中でも「今見えている範囲」だけを優先できるようにする。

実施内容:
- `ScrollViewer` と item container から visible range を求める `UpperTabViewportTracker` を導入する。
- visible / near-visible / hidden の 3 段状態を作る。
- スクロールイベントは 16ms から 33ms 程度で間引く。
- visible range 変化時のみ UI 更新 dirty を立てる。

新規候補:
- `UpperTabs/Common/UpperTabViewportTracker.cs`
- `UpperTabs/Common/UpperTabVisibleRange.cs`

対象候補:
- `MainWindow.xaml.cs`
- 上側タブの各 partial

完了条件:
- active tab の可視先頭/末尾 index が取れる。
- スクロール中の更新が全件ではなく visible 中心になる。

### Phase 3: visible-first のサムネ優先制御

目的:
- 描画だけでなく、サムネイル生成順も visible-first に寄せる。

実施内容:
- queue request に visible-first 用 priority を追加する。
- 現在の `TabIndex` 優先に加え、同一 tab 内で visible 領域を最上位にする。
- `Visible`、`NearVisible`、`Background` の 3 段 priority を導入する。
- 既存の `ThumbPanelPos` は流用せず、別の priority 情報を持つ。

新規候補:
- `UpperTabs/Common/UpperTabThumbnailPriorityScheduler.cs`

対象候補:
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/QueueRequest.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/QueueDbService.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`

完了条件:
- visible 範囲のサムネ要求が、同一タブ内の背景要求より先に処理される。

### Phase 4: 上側タブの責務分割

目的:
- 高速化の基盤を、タブ単位で保守しやすい構成へ落とす。

実施内容:
- `UpperTabs/Common` を追加し、活性判定、viewport、priority を共通化する。
- `Small / Big / Grid / Big10 / List` ごとの partial を作る。
- 必要なら XAML host 化を段階導入する。
- `MainWindow` は shell に寄せる。

新規候補:
- `UpperTabs/Small`
- `UpperTabs/Big`
- `UpperTabs/Grid`
- `UpperTabs/Big10`
- `UpperTabs/List`

完了条件:
- 上側タブの高速化ロジックが `MainWindow.xaml.cs` に散らばらず、タブごとに追える。

## 6. 実装順の判断

優先順位は固定する。

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3
5. Phase 4

理由:
- 先に `decode 最適化` と `非アクティブ停止` を入れる方が工数に対する効きが大きい。
- visible-first queue は効果が大きいが、viewport 情報が無いと正しく組めない。
- host 化は保守性には効くが、速度そのものには直結しない。

## 7. タブ別の扱い

### 7.1 `Small / Big / Grid / Big10`

- 同じ「サムネ主役タブ」として共通基盤に乗せる。
- ただし item template は共通化しない。

### 7.2 `List`

- `DataGrid` のまま扱う。
- 活性判定、visible-first priority、decode 方針だけ共通基盤に乗せる。
- 描画 host は別線で考える。

## 8. リスク

- visible range 取得方法を誤ると、スクロール中に更新が揺れる。
- decode サイズを攻めすぎると画質劣化が目立つ。
- queue priority の導入で既存の manual enqueue と衝突すると、期待順序が崩れる。
- `FilteredMovieRecs` 全件差し替え構造が残るため、Phase 3 まで入れても一部の揺れは残る可能性がある。

## 9. テスト観点

- `Small / Big / Grid / Big10 / List` の各タブで、初回表示時のサムネ出現順が visible-first になるか。
- 高速スクロール時に UI フリーズが悪化していないか。
- tab 切替直後に、非アクティブ tab の更新が走っていないか。
- decode サイズ導入後も、サムネ表示が崩れないか。
- manual enqueue と rescue 系の挙動が変わっていないか。

## 10. 最初の着手対象

最初の 1 本は Phase 1 にする。

具体的には:
- `UpperTabs/Common/UpperTabActivationGate.cs`
- `UpperTabs/Common/UpperTabDecodeProfile.cs`
- `MainWindow.xaml`
- `Converter/NoLockImageConverter.cs`

この順なら、visible-first の本丸へ入る前に、無駄な描画負荷をかなり落とせる。
