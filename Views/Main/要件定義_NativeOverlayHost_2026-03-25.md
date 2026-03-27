# NativeOverlayHost 要件定義書（2026-03-25）

## 1. 目的
- UI 応答低下通知を、`MainWindow` の UI スレッドから分離した overlay スレッドで表示する。
- `HwndSource` ベース（ネイティブ）を第一手段とし、失敗時は WPF fallback で可視性を保つ。
- `UiHangNotificationCoordinator` の表示ルール（Critical 優先、Foreground制御）を壊さずに表示を担保する。

## 2. 対象範囲
- 主要対象: `Views/Main/NativeOverlayHost.cs`
- 連携先:
  - `Views/Main/UiHangNotificationCoordinator.cs`（表示要求）
  - `Views/Main/MainWindow.UiHangNotification.cs`（配置情報）
- ログカテゴリ: `ui-overlay`

## 3. 用語
- `native`: `HwndSource` + `UpdateLayeredWindow` / `SetLayeredWindowAttributes` 経路
- `fallback`: ネイティブ失敗時に WPF `Window` を使う代替経路
- `placement`: `UiHangOverlayPlacement.Bounds` から算出する表示位置情報

## 4. 機能要件

### 4.1 ライフサイクル
1. `Start()` は 1 本の STA スレッドを起動し、overlay 用 `Dispatcher` を開始する。
2. `Stop()` は更新要求受け付け停止→現在表示の `Hide`→`Dispatcher` シャットダウン→スレッド終了を行う。
3. `Dispose()` は `Stop()` を通して呼ばれ、2回目以降の破棄でも例外を上位に上げない。
4. 複数回 `Start()`/`Stop()` が来ても整合性を保つ。

### 4.2 API
1. `Show(level, message)` は `force_show=true` で状態更新し、表示要求を受ける。
2. `Update(level, message)` は既存表示の更新を行い、必要なら再描画する。
3. `Hide()` は即時非表示要求を受け付ける。
4. `UpdatePlacement(UiHangOverlayPlacement)` は次回描画に反映するために配置状態を保持する。
5. レベルとメッセージは内部状態として保持し、再描画で再利用する。

### 4.3 native 表示要件
1. 初期表示は native 表示を優先し、`WS_EX_TOPMOST / WS_EX_TOOLWINDOW / WS_EX_NOACTIVATE / WS_EX_TRANSPARENT / WS_EX_LAYERED` を使用する。
2. サイズは `width=460`,`height=48`、配置は下方向 24px の余白を確保し中央寄せとする。
3. 位置計算は `placement.Bounds` 基準だが、モニタ作業領域内へクランプする。
4. 初期化でレイヤードスタイル適用または `SetLayeredWindowAttributes` に失敗した場合は native 開始失敗として fallback へ移行する。
5. 描画時は `UpdateLayeredWindow` 失敗で即座に fallback 移行し、以降 native 更新を継続しない。

### 4.4 fallback 表示要件
1. WPF Window フォールバックは `ShowInTaskbar=false`、`WindowStyle=None`、`ResizeMode.NoResize`、`Topmost=true`、`AllowsTransparency=true` を維持する。
2. `Yu Gothic UI`、`FontSize=15`、1行省略（`CharacterEllipsis`）を使用する。
3. 背景黒、文字色はレベル別、透明度は 60%（`Opacity=0.6`）。
4. multi-monitor 時は `GetDpiForMonitor` の DPI で DIP 換算した座標を使う。

### 4.5 例外・堅牢性
1. 描画スレッド上の Win32 例外は握り潰し、ログのみ残してアプリ継続を優先する。
2. overlay が未初期化でも `Show/Update/Hide` が破壊的に失敗しないこと。
3. 文字列は `null` 許容とし、表示時は空文字として扱う。

### 4.6 ログ要件
`ui-overlay` に以下を記録する。
1. overlay スレッド開始・終了
2. native 作成時の成功/失敗（exstyle、alpha適用）
3. native→fallback への切替
4. 描画成功/失敗（`hwnd`,`error`,`x`,`y`）
5. 表示/非表示要求（level/force_show/message）

## 5. 非機能要件
1. MainUI スレッドとは独立して、通知描画が応答性を阻害しない。
2. 連続ハング状態でも overlay スレッドは復帰可能であること。
3. レベル文言変更や位置更新が高頻度でもブレークせず描画できること。
4. 表示座標・色・透明度の整合ログが取りやすいこと。

## 6. 受け入れ条件
1. `native` 失敗が継続しても fallback で表示要求が成立する。
2. `OverlayAlpha=153`（60%）を反映可能なら適用し、失敗時も fallback 続行で可視化を優先する。
3. `placement` が空でも `SystemParameters.WorkArea` を使って表示できる。
4. `Hide()` の後、短時間（1 秒以内）で画面上から消える。
5. 位置計算が常にモニタ作業領域から外れない。

## 7. 参考
- `Views/Main/NativeOverlayHost.cs`
- `Views/Main/障害対応_オーバーレイFallback表示改善_2026-03-25.md`



## ✅ 検証プラン

### 自動テスト
既存テスト [UiHangNotificationPolicyTests.cs](file:///C:/Users/na6ce/source/repos/IndigoMovieManager_fork_workthree/Tests/IndigoMovieManager.Tests/UiHangNotificationPolicyTests.cs) は [MainWindow](file:///C:/Users/na6ce/source/repos/IndigoMovieManager_fork_workthree/Views/Main/MainWindow.UiHangNotification.cs#7-237) の静的メソッド（[IsUiHangDangerStateCore](file:///C:/Users/na6ce/source/repos/IndigoMovieManager_fork_workthree/Views/Main/MainWindow.UiHangNotification.cs#176-195) / [ShouldDisplayUiHangNotificationCore](file:///C:/Users/na6ce/source/repos/IndigoMovieManager_fork_workthree/Views/Main/MainWindow.UiHangNotification.cs#208-227)）のテストで、今回の変更対象外のため、まず壊れないことを確認する。

```powershell
dotnet test C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj --filter "UiHangNotificationPolicyTests" -c Release --platform x64
```

### ビルド確認
```powershell
dotnet build C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\IndigoMovieManager.sln -c Release --platform x64
```

### 手動確認（ユーザーにお願い！🙏）
[NativeOverlayHost](file:///C:/Users/na6ce/source/repos/IndigoMovieManager_fork_workthree/Views/Main/NativeOverlayHost.cs#12-1164) は別スレッドの HwndSource を使うためユニットテスト化が難しい。以下を目視確認してもらうのが一番確実！

1. アプリ起動後、`ui-overlay` ログに `overlay thread created` が出ること
2. UI詰まり発生時に fallback 経路で通知バナーが画面下中央に表示されること
3. `ui hang recovered` 後にバナーが消えること
4. 複数モニタ環境で通知が画面外に飛ばないこと

> [!TIP]
> 今回は**純粋な内部リファクタリング（外部API変更なし）**なので、既存の動作が壊れなければOKだよ！
