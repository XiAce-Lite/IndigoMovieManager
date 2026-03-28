# Plan: 詳細パネル画像非表示バグ修正 + RefreshDetailThumbnailImage リファクタ

## TL;DR

下部詳細パネルでテキスト情報は更新されるが画像が表示されないバグ。
根本原因は `ExtDetail.xaml.cs` の `RefreshDetailThumbnailImage` にある 2 重の不具合：
1. `Source = null` が MultiBinding を破壊している
2. `GetBindingExpression` が MultiBinding に対して null を返す（MultiBinding には `GetMultiBindingExpression` が必要）

修正は `RefreshDetailThumbnailImage` の 2 行を正す最小変更 + 同メソッド周辺の整理。

---

## 根本原因の詳細

### バグの発生フロー

1. ユーザーがメイン一覧で動画を左クリック
2. `List_SelectionChanged` → `ShowExtensionDetail(mv)` が呼ばれる
3. `ShowExtensionDetail` → `EnsureActiveExtensionDetailThumbnail(record)` で `ThumbDetail` パスを設定
4. → `RefreshActiveExtensionDetailTab(record)` → `ExtensionTabViewHost.ShowRecord(record)`
5. `ShowRecord` が `DataContext = record` を設定
6. `DataContextChanged` fires → `ApplyConfiguredDetailThumbnailMode()`
7. → `ApplyThumbnailDisplaySizeForCurrentContext(mode)` → `RefreshDetailThumbnailImage(forceRebind: true)`
8. **ここでバグ発動**: `DetailThumbnailImage.Source = null` が **MultiBinding を破壊**
9. → `GetBindingExpression(Image.SourceProperty)` が **null を返す**（MultiBinding だから）
10. → `binding?.UpdateTarget()` は **実行されない**
11. → **画像は null のまま表示されない**

### なぜテキスト情報は正しく更新されるか

テキスト（ファイル名、パス、サイズ等）は通常の `{Binding}` で DataContext にバインドされており、
DataContext 切替時に自動で再評価される。画像だけが `ConverterBindableParameter`（= MultiBinding MarkupExtension）
を使っており、上記の 2 重バグで壊される。

### 技術的根拠

- `ExtDetail.xaml` の `Image.Source` は `ConverterBindableParameter` MarkupExtension で設定
- `ConverterBindableParameter.ProvideValue()` は内部で **MultiBinding** を生成して返す
- WPF の `FrameworkElement.GetBindingExpression(dp)` は **Binding 専用**。MultiBinding には null を返す
- MultiBinding には `BindingOperations.GetMultiBindingExpression(element, dp)` が必要
- さらに `Image.Source = null` は WPF のローカル値設定なので、**既存 MultiBinding を上書きして破壊する**

---

## Phase A: 根本修正

### Step 1 — RefreshDetailThumbnailImage の修正（*独立*）

対象: `UserControls/ExtDetail.xaml.cs` の `RefreshDetailThumbnailImage` メソッド（行 61–72 付近）

修正内容:

1. `DetailThumbnailImage.Source = null` を **削除する**
   - ローカル値設定は MultiBinding を破壊するため
   - 次の `UpdateTarget()` で Converter が再評価されるので、キャッシュ無効化済みの画像は新しい値で再読み込みされる

2. `DetailThumbnailImage.GetBindingExpression(Image.SourceProperty)` を
   `BindingOperations.GetBindingExpressionBase(DetailThumbnailImage, Image.SourceProperty)` に変更する
   - `GetBindingExpressionBase` は Binding / MultiBinding / PriorityBinding いずれでも動く汎用メソッド

修正後のコード概形:
```csharp
private void RefreshDetailThumbnailImage(bool forceRebind = false)
{
    Dispatcher.BeginInvoke(new Action(() =>
    {
        BindingExpressionBase binding = BindingOperations.GetBindingExpressionBase(
            DetailThumbnailImage, Image.SourceProperty);
        binding?.UpdateTarget();
    }));
}