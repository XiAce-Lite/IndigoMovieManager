# ダークモード対応方針 🌙✨

現在 `MaterialDesignThemes` を使っているから、`App.xaml` の設定を少し変えるだけでダークモード対応が爆速でできちゃうよ！🥰
以下のアプローチが考えられるので、どっちが良いか選んでみてね！

## パターン1: OSの設定を自動適用する（おすすめ！超絶シンプル💖）
一番手っ取り早くて今風なのは、Windowsの「色」設定に合わせてアプリ起動時に自動で切り替わるようにすること！

**修正内容 (`App.xaml`)**:
```xml
<materialDesign:BundledTheme
    BaseTheme="Inherit" <!-- 🌟 ここを Light から Inherit に変更するだけ！ -->
    PrimaryColor="Indigo"
    SecondaryColor="DeepPurple" />
```
*※起動時の連動になるよ。*

## パターン2: アプリ内で動的に切り替える（トグルスイッチ等で制御🔥）
「アプリ内の手動設定で明示的にダークモードにしたい！」という場合は、コードからテーマを動的に変更するアプローチになるよ。

**対応のステップ**:
1. UI（設定画面など）に切り替え用のスイッチを配置。
2. コードビハインドか ViewModel で `PaletteHelper` を使ってテーマを上書きする。

**実装イメージ**:
```csharp
using MaterialDesignThemes.Wpf;

public void ToggleBaseTheme(bool isDark)
{
    var paletteHelper = new PaletteHelper();
    var theme = paletteHelper.GetTheme();
    theme.SetBaseTheme(isDark ? Theme.Dark : Theme.Light);
    paletteHelper.SetTheme(theme);
}
```

## 注意点: ダークテーマ化における「罠」の調整 🎨
ダークテーマにした場合、以下のような箇所が見えづらくなるかもしれないので追加対応が必要になるかも…！
* `Dirkster.AvalonDock.Themes.VS2013` の見た目調整（AvalonDockのDarkテーマを当てる等）
* ハードコードされている一部のコントロールの背景色や文字色
* 背景が透過された黒系のアイコン画像

---
とりあえずサクッと「OS連動（Inherit）」を試してみるのがおすすめだよ！どっちの方針で進めよっか？✨
