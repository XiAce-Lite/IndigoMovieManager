# BottomTabs Debug 切り替え方針

## 目的

- `Debug` 系タブ（`Debug` / `Log` / `サムネ失敗Debug`）の表示/非表示を
  `Release` と `Debug` ビルドで確実に切り分ける。
- レイアウト復元やタブの再配置で「非表示設定が再度見えてしまう」問題を防ぐ。
- 将来の保守で、原因切り分けを `MainWindow` 初期化フローと対応付けて追える状態にする。

## 対象

- `BottomTabs/DebugTab/MainWindow.BottomTab.Debug.cs`
- `BottomTabs/LogTab/MainWindow.BottomTab.Log.cs`
- `BottomTabs/ThumbnailError/MainWindow.BottomTab.ThumbnailError.cs`
- `Views/Main/MainWindow.xaml.cs`
- `Views/Main/MainWindow.xaml`

## 読み方

まずは次の順で読む。

1. `表示判定フラグ`
   - 何を基準に Debug 系タブを出し分けるか掴む。
2. `レイアウト復元（起動時）`
   - 旧 `layout.xml` とどう整合を取るかを見る。
3. `非表示時の安全処理`
   - `Hide()` とレイアウト木除去のどちらを使うかを見る。
4. `トラブル時の確認手順`
   - 実際に壊れた時の当たり先を確認する。

## 役割サマリ

- この資料は `Debug` / `Log` / `サムネ失敗Debug` の表示切り替えを、`判定 / 復元 / 非表示` の3段で追うためのもの。
- 目的は「Release で見えないこと」だけでなく、「古い layout 復元で再露出しないこと」を守ること。
- 実装を直す時は、`判定関数` と `Apply*Visibility` と `TryRestoreDockLayout` を対で見る。

## 1. 表示判定フラグ

### `Debug` タブ

- 判定は `EvaluateShowDebugTab()` で行う。
- `#if DEBUG` 時のみ `!IsReleaseBuild()` を返す。
- `IsReleaseBuild()` は `AssemblyConfigurationAttribute` を見て `Release` を比較する。
- `ShouldShowDebugTab` はこの判定結果を保持した static readonly 変数。

### `Log` タブ

- `Log` も `ShouldShowDebugTab` をそのまま使う。
- 判定源を増やさず、Debug 系タブの可視条件を 1 か所へ固定している。

### `サムネ失敗Debug` タブ

- 表示判定は `Debug` と同じ評価を使うため、`ShouldShowThumbnailErrorBottomTab` を
  `EvaluateShowDebugTab()` を参照するプロパティ化した。
- `static readonly` のエイリアスを外して、判定元を1つに揃えた。

## 2. レイアウト復元（起動時）

- `MainWindow.xaml.cs` の `TryRestoreDockLayout()` は `layout.xml` を読む。
- 復元前に必須タブの存在を検証する。
  - `ToolThumbnailProgress` が無い場合は既定レイアウトへフォールバック。
  - `ToolThumbnailError` は `ShouldShowThumbnailErrorBottomTab` が true の時だけ必須扱い。
  - `ToolDebug` は `ShouldShowDebugTab` が true の時だけ必須扱い。
  - `ToolLog` も `ShouldShowDebugTab` が true の時だけ必須扱い。
- Release なら、上記判定で Debug 系タブが必須化されないため、
  復元自体で旧レイアウトへ戻されにくい。

## 3. 非表示時の安全処理

### `Debug` タブ（`ApplyDebugTabVisibility`）

- `ShouldShowDebugTab == false` のとき、
  `IsSelected/IsActive=false` し、`Hide()` を呼んで残存表示を止める。
- `MainWindow.xaml` 初期化で `ApplyDebugTabVisibility()` は必ず呼ばれるため、
  起動後にも表示状態を再強制する。

### `Log` タブ（`ApplyLogTabVisibility`）

- `ShouldShowDebugTab == false` のとき、
  `IsSelected/IsActive=false` し、`Hide()` を呼んで残存表示を止める。
- 旧レイアウト復元で別ペインへ流れても、`uxAnchorablePane2` へ戻す。
- `Log` タブ側のタイマーは、表示中だけ末尾プレビューを更新する。
- `Debug` タブのログ表示は撤去し、プレビュー責務は `Log` に一本化した。

### `サムネ失敗Debug` タブ（`ApplyThumbnailErrorBottomTabVisibility`）

- `ShouldShowThumbnailErrorBottomTab == false` のとき、
  `IsSelected/IsActive=false`。
- `ThumbnailErrorBottomTab.Parent` を辿って現在のレイアウトコンテナから除去（`RemoveChild`）。
- さらに `uxDockingManager.Layout` を再帰走査し、`ContentId="ToolThumbnailError"` を持つ
  ノードをレイアウト木から除去する。
  - `CanHide=false` の旧状態が残っていて `Hide()` だけで消えないケースへの耐性。
- これで `Release` で残っていたタブ名表示を抑止しやすくする。

## 4. 可視判定・ポーリング連携

- `BottomTabs/Common/BottomTabActivationGate.cs` の
  `IsVisibleOrSelected(LayoutAnchorable tab)` で「見えている/選択中」の判定を共通化。
- `Debug` / `Log` は `PropertyChanged` の監視でアクティブ判定に応じてタイマーを起動・停止。
- `サムネ失敗` は `HasThumbnailErrorBottomTabHost()` / `IsThumbnailErrorTabActiveCached()` による
  可視判定を経由してポーリング実行。
- 非表示中は `dirty` を保持し、再表示時に反映する設計。

## 5. トラブル時の確認手順

1. `BottomTabs/DebugTab/MainWindow.BottomTab.Debug.cs` の
   `EvaluateShowDebugTab` が想定どおりか確認。
2. `BottomTabs/LogTab/MainWindow.BottomTab.Log.cs` の
   `ApplyLogTabVisibility` が `uxAnchorablePane2` へ戻しているか確認。
3. `BottomTabs/ThumbnailError/MainWindow.BottomTab.ThumbnailError.cs` の
   `ShouldShowThumbnailErrorBottomTab` がプロパティになっているか確認。
4. `Views/Main/MainWindow.xaml.cs` の `TryRestoreDockLayout` で
   `missing-debug-tool` / `missing-log-tool` / `missing-thumbnail-error-bottom-tab` のバックアップ判定が
   入っているか確認。
5. `layout.xml` に `ToolDebug` / `ToolLog` / `ToolThumbnailError` が残っていても
   起動後に消えるか `Apply*Visibility` の実行結果で確認。

## 方針メモ（重要）

- 今後新たに `Debug` 依存の下部タブを追加する場合、
  `EvaluateShowDebugTab` を経由した判定に統一し、
  `Apply*Visibility` で `Parent` 除去 or `Hide()` の二段で閉じる。
- レイアウト木そのものに対して `ContentId` での除去処理を併記すると、
  `CanHide=false` や復元履歴由来の再露出リスクが下がる。

## 運用メモ

- 下部タブ資料も、まず `役割 / 読み方 / 要点` の順で読める形を保つ。
