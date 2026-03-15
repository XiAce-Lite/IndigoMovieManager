# Implementation Plan: 下部タブ分割 Phase 1 サムネイル進捗 (2026-03-15)

## 実装反映メモ (2026-03-15)

- `BottomTabs/ThumbnailProgress/MainWindow.BottomTab.ThumbnailProgress.cs` を追加し、進捗タブ固有のタイマー、設定UI、差分反映要求、表示判定をここへ移した。
- `BottomTabs/ThumbnailProgress/ThumbnailProgressTabVisibilityGate.cs` を追加し、AvalonDock の表示中/選択中判定をここへ寄せた。
- `MainWindow.xaml.cs` には、全体ライフサイクル側の初期化呼び出し `InitializeThumbnailProgressUiSupport()` だけを残した。
- hidden 中は dirty だけ立て、再表示時に最新スナップショットを 1 回だけ反映する形まで Phase 1 で入れた。

## 1. 目的

- 下部タブ分割の第一歩として、`サムネイル進捗` タブを独立した責務単位へ切り出す。
- `MainWindow.xaml.cs` に集中している進捗タブ固有ロジックを、将来 `BottomTabs/ThumbnailProgress` へ移せる形に整える。
- 「全体の ON/OFF は上位」「細かい調整は進捗タブ関連コード」の境界を先に作る。

## 2. 対象

- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `Thumbnail/MainWindow.ThumbnailFailureSync.cs`
- `Thumbnail/Adapters/ThumbnailProgressUiMetricsLogger.cs`

## 3. Phase 1 の結論

- まずは新規フォルダ `BottomTabs/ThumbnailProgress` を作る。
- `MainWindow.xaml` のタブ本体は直置きのままでよい。
- 先にコード責務だけを `BottomTabs/ThumbnailProgress` の partial / presenter へ移す。
- 進捗 UI 更新の可否は、次の二段で制御する。
  - 上位: `MainWindow` 側の最上位 ON/OFF
  - 下位: `ThumbnailProgress` タブ側の表示中判定と dirty 管理

## 4. 目標構成

```text
BottomTabs/
  ThumbnailProgress/
    MainWindow.BottomTab.ThumbnailProgress.cs
    ThumbnailProgressTabController.cs
    ThumbnailProgressTabVisibilityGate.cs
```

Phase 1 では View 分離まではやらない。

## 5. 分離する責務

### 5.1 `MainWindow` に残すもの

- アプリ全体のライフサイクル
- タイマーの生成と破棄
- Dock 全体の構成
- 他タブと共通の最上位 ON/OFF

### 5.2 `BottomTabs/ThumbnailProgress` へ寄せるもの

- 進捗タブ更新条件
- 進捗タブが表示中かどうかの判定
- hidden 中の dirty 管理
- `UpdateThumbnailProgressSnapshotUi()` の適用条件
- `ForceThumbnailProgressSnapshotRefreshNow()` の進捗タブ向け条件
- 進捗タブ設定 UI の同期

## 6. 実装方針

### 6.1 最上位 ON/OFF

- `MainWindow` 側に薄い gate を残す。
- 役割は「アプリ全体として進捗 UI 更新を許すか」だけに絞る。

候補:

- `IsThumbnailProgressUiEnabled()`

ここではタブの可視状態までは見ない。

### 6.2 タブ固有の細かい判定

- `BottomTabs/ThumbnailProgress` 側に閉じる。
- 役割は「今このタブへ反映してよいか」を決めること。

候補:

- `IsThumbnailProgressTabVisibleOrSelected()`
- `ShouldApplyThumbnailProgressUi()`
- `MarkThumbnailProgressUiDirtyWhileHidden()`
- `TryApplyThumbnailProgressUiIfVisible()`

### 6.3 dirty 管理

- タブ非表示中は毎回 `ApplySnapshot(...)` しない。
- ただし差分は捨てず、dirty フラグだけ立てる。
- タブが表示された瞬間に最新スナップショットを1回だけ反映する。

### 6.4 既存 producer / consumer は触らない

- `ThumbnailCreation`
- `ThumbnailQueue`
- `ThumbnailFailureSync`

これらは引き続き `RequestThumbnailProgressSnapshotRefresh()` を投げるだけにする。

## 7. 具体的な変更ステップ

### Step 1

- `BottomTabs/ThumbnailProgress` フォルダを新設
- `MainWindow.BottomTab.ThumbnailProgress.cs` を追加

### Step 2

- `MainWindow.xaml.cs` から次を移す
  - `UpdateThumbnailProgressConfiguredParallelism`
  - `ForceThumbnailProgressSnapshotRefreshNow`
  - `ThumbnailProgressUiTimer_Tick`
  - `UpdateThumbnailProgressSnapshotUi`
  - `RequestThumbnailProgressSnapshotRefresh`
  - `ProcessThumbnailProgressSnapshotRefreshQueue`
  - `EnsureThumbnailProgressUiTimerRunning`
  - `SyncThumbnailProgressSettingControls`
  - 進捗タブ設定 UI のイベント周辺

### Step 3

- `ThumbnailProgressTabVisibilityGate.cs` を追加
- `ThumbnailProgressTab` の表示状態判定をここへ寄せる

### Step 4

- `hidden 中は dirty のみ、visible 時だけ apply` を入れる

### Step 5

- `MainWindow_ContentRendered` とタブ表示変化時の初回反映を整える

## 8. タブ表示判定の扱い

Phase 1 では厳密な「選択中」だけに寄せすぎない。

判定の優先順:

1. `ThumbnailProgressTab` が存在する
2. `IsHidden == false`
3. `IsVisible` 相当で描画可能
4. 可能なら `IsSelected` も加味する

AvalonDock 依存のため、最初は保守的に「表示中または選択中」で扱う。

## 9. 非目標

- 下部タブ全部の同時分割
- XAML の大規模分解
- `Bookmark` / `Debug` / `Extension` の同時移行
- 進捗 ViewModel の全面刷新

## 10. 次フェーズへの接続

### Phase 2 候補

- `BottomTabs/Debug`
- `BottomTabs/Bookmark`

### Phase 3 候補

- `BottomTabs/Extension`
- `BottomTabs/SavedSearch`

## 11. 受け入れ条件

- `MainWindow.xaml.cs` から進捗タブ固有コードが見て分かる量だけ減る
- 進捗 UI 更新の可否判定が `ThumbnailProgress` 側へ寄る
- producer / consumer は更新要求を投げるだけのまま維持される
- タブ非表示中に無駄な `ApplySnapshot(...)` が走らない
- タブ再表示時に最新状態へ追いつく

## 12. リスク

- AvalonDock の表示状態判定が直感通りでない可能性がある
- hidden 中の dirty 管理を誤ると、再表示時に古い表示が残る
- 設定 UI 同期まで一気に動かすと差分が大きくなる

## 13. 実装順の提案

最初の PR / コミット系列は 2 段に分ける。

1. `BottomTabs/ThumbnailProgress` 導入とコード移動だけ
2. 「表示中だけ反映」gate と dirty 制御

この順なら、責務整理と挙動変更を分けて追える。
