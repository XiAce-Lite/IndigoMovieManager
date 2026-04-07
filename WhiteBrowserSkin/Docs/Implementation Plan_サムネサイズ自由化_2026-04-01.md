# Implementation Plan: サムネサイズ自由化 2026-04-01

最終更新日: 2026-04-01

変更概要:
- WhiteBrowser 互換の「スキン側でサムネ表示サイズを決める」思想を、現行 IndigoMovieManager の構造へどう持ち込むかを整理した
- 現状の固定サイズ前提が、表示層 / decode 層 / 生成層 / 保存先に跨って埋まっていることを明文化した
- 「表示サイズの自由化」と「生成サイズの自由化」を分離し、段階導入する方針を固定した
- 外部 WB スキンと built-in WPF タブを同じやり方で無理に揃えず、二系統の実装計画として整理した

## 1. 結論

この要望は入れる価値が高い。
ただし、**「数字を設定画面で自由入力できるようにする」だけでは破綻する**。

理由は単純で、現行実装ではサムネサイズが次の 4 層へまたがって固定化されているからである。

1. WPF の表示サイズ
2. 画像 decode の目安サイズ
3. サムネ生成のレイアウトサイズ
4. サムネ保存先フォルダ名

したがって、正しい切り方は次の二段構えである。

1. **表示サイズの自由化**
   - まずは UI 上の見え方だけ自由化する
   - 既存の生成資産はそのまま再利用する
2. **生成サイズの自由化**
   - 必要になった時だけ、保存先と生成レイアウトも自由化する

この順で進めないと、既存サムネ資産、Queue、Watcher、失敗同期、thumb root 規約まで一気に壊れる。

## 2. WhiteBrowser との対応づけ

WhiteBrowser は、サムネ表示サイズを **HTML/CSS + `div#config`** で決める。
つまり本質は「設定値」ではなく **スキン定義** である。

現行 IndigoMovieManager でも、その流れ自体はすでに一部入っている。

- 外部スキンの `div#config` は `WhiteBrowserSkinCatalogService` が読める
- `thum-width` / `thum-height` / `thum-column` / `thum-row` もパース済み
- ただし今は **既存 5 タブへ丸めて寄せるだけ** になっている

したがって今回の本筋は、

- 外部 WB スキンでは、`thum-*` を丸めずに表示へ効かせる
- built-in WPF タブでは、CSS の代わりに `DisplayProfile` を導入する

である。

## 3. 現状の固定点

### 3.1 WPF built-in タブ

固定サイズが XAML に直書きされている。

- `Small`: `288x72`（= `120x90x3x1` の横並び表示）
- `Big`: `600x150`（= `200x150x3x1`）
- `Grid`: `160x120`
- `List`: DataGrid 列幅固定
- `Big10`: `600x180`（= `120x90x5x2`）

ここは単に画像サイズだけでなく、タイトル幅、タグ幅、詳細コントロール幅まで連動している。

### 3.2 decode 目安

`UpperTabDecodeProfile` が各タブごとに固定 decode 高さを持っている。

- Small = 90
- Big = 150
- Grid = 120
- List = 42
- Big10 = 180

表示自由化を入れるなら、ここも固定値から外す必要がある。

### 3.3 生成サイズ

`ThumbnailLayoutProfileResolver` が、生成シートサイズと保存先フォルダ名の両方を固定している。

- Small = `120x90x3x1`
- Big = `200x150x3x1`
- Grid = `160x120x1x1`
- List = `56x42x5x1`
- Big10 = `120x90x5x2`

### 3.4 保存先

`ThumbnailLayoutProfile.FolderName` は `{Width}x{Height}x{Columns}x{Rows}` であり、
`ResolveThumbnailOutPath(...)` がこの値をそのまま保存先フォルダへ使っている。

つまりサイズ自由化は、

- 画像の見え方変更
- 生成サイズ変更
- 保存先変更

を同時に意味してしまう。

## 4. 採用方針

### 4.1 大原則

**表示サイズプロファイル** と **生成サイズプロファイル** を分離する。

### 4.2 新しい責務分離

1. `ThumbnailDisplayProfile`
   - UI 表示サイズ
   - decode 目安
   - タイトル/タグ/詳細のレイアウト幅
   - スクロール密度

2. `ThumbnailLayoutProfile`
   - 生成シートサイズ
   - WB互換 metadata
   - 保存先フォルダ名

3. `ThumbnailDisplayProfileResolver`
   - 現在の skin / tab / profile から、UI に使う表示サイズを返す

4. `ThumbnailLayoutProfileResolver`
   - 既存どおり、生成と保存先の正本を返す

この 2 つを最初から同じ型にしないこと。
ここを混ぜると、表示調整だけで Queue/Watcher/Index 修正が必須になる。

## 5. フェーズ分割

## Phase 1: 外部 WB スキンの表示サイズ自由化

### 5.1 目的

外部 WhiteBrowser スキンでは、`thum-width` / `thum-height` / `thum-column` / `thum-row` を
**丸めずに表示へ反映**する。

### 5.2 方針

- WebView2 表示時は、外部スキンの HTML/CSS を正本とする
- `WhiteBrowserSkinCatalogService.ResolvePreferredTabStateName(...)` は
  フォールバック用に残すが、通常の外部スキン表示では主経路にしない
- `WhiteBrowserSkinConfig` の `ThumbWidth/Height/Column/Row` を、WebView 側描画へそのまま渡す
- 生成サムネ自体は既存資産をそのまま使う

### 5.3 この段階でやらないこと

- custom サイズ専用の管理サムネ生成
- custom サイズ専用フォルダ作成
- WPF built-in タブの自由化

### 5.4 完了条件

- 外部スキンで `thum-width` / `thum-height` を変えると、表示サイズが変わる
- `thum-column` / `thum-row` に応じてスキン側レイアウトが変わる
- Runtime 未導入時の既存フォールバックは壊さない

## Phase 2: built-in WPF タブの表示サイズ自由化

### 5.5 目的

DefaultSmall / Big / Grid / List / Big10 でも、表示サイズを固定値から外す。

### 5.6 方針

- `ThumbnailDisplayProfile` を新設する
- built-in タブは、XAML 直書きサイズを `Binding` / `DynamicResource` / `Style` へ逃がす
- `UpperTabDecodeProfile` は固定クラスではなく、`ThumbnailDisplayProfile` から解決する
- `Small/Big/Big10` の周辺テキスト幅や詳細幅も profile 連動にする

### 5.7 保存先

built-in の表示サイズ設定は、まず次のどちらかへ保存する。

1. DB ごとの `profile` テーブル
2. built-in 擬似 skin 名ごとの profile

推奨は **profile テーブル** である。
理由は、WhiteBrowser の「スキンごとに見え方が変わる」という思想に近く、
DB 単位の使い分けとも整合しやすいから。

### 5.8 UI 案

設定画面へ次を追加する。

- 表示サイズプリセット
  - Small
  - Middle
  - Large
  - Custom
- Custom 時の
  - 幅
  - 高さ
  - 列数
  - 行数

ただし最初の実装では、**自由入力でも内部ではガードをかける**。

- 幅: `48..640`
- 高さ: `36..480`
- 列数: `1..10`
- 行数: `1..10`

### 5.9 完了条件

- built-in タブでも表示サイズが profile で変わる
- decode サイズが追従する
- 一覧テンポが許容範囲に収まる
- 既存 DB を開いても従来サイズで崩れない

## Phase 3: 表示自由化のまま既存生成資産を再利用

### 5.10 目的

表示だけ自由化した状態で、既存の generated jpg をできるだけ再利用する。

### 5.11 方針

- 生成元は引き続き既存 5 レイアウト + detail を使う
- 表示サイズが custom でも、まずは最寄りの既存資産を使って縮小/拡大表示する
- 同名画像 fallback / source-image-import はそのまま利用する

### 5.12 理由

ここで custom 生成まで一気にやらない理由は次のとおり。

- thumb フォルダが増殖する
- Queue と未作成判定が複雑化する
- FailureSync / Rescue / IndexRepair の責務が膨らむ
- 体感テンポ最優先の本線方針と衝突しやすい

### 5.13 品質基準

- まずは「少しぼけても使える」を許容する
- その代わり、初動と安定性を守る

## Phase 4: custom 生成サイズの導入

### 5.14 目的

本当に必要な場合だけ、custom 表示サイズに合わせた専用生成を導入する。

### 5.15 前提

この段階は Phase 1〜3 が安定してからでよい。
最初からここへ入るのは危険である。

### 5.16 必須作業

- `ThumbnailLayoutProfile` を custom 対応へ拡張
- 保存先フォルダ戦略を決める
  - 例: `{Width}x{Height}x{Columns}x{Rows}`
  - ただし乱立防止策が必要
- `ResolveThumbnailOutPath(...)` の互換戦略を追加
- Queue / FailureSync / Watcher / ThumbIndexRepair の custom 対応
- detail / rescue / worker / metadata writer の custom 対応

### 5.17 非推奨案

「表示サイズを変えたら毎回そのサイズの管理サムネを別生成する」は非推奨。
ユーザーがサイズをいじるたびにフォルダが増え、保守不能になる。

### 5.18 推奨案

custom 生成が必要でも、

- 保存対象は明示的なプリセットだけ
- 完全自由入力は表示専用

と分ける方が安全である。

## 6. 具体的な設計判断

### 6.1 `WhiteBrowserSkinCatalogService`

- 今の `ResolvePreferredTabStateName(...)` は built-in fallback 用として維持
- ただし外部スキン表示の主経路では、「どの既存タブへ寄せるか」ではなく
  「config をそのまま表示へ使う」方向へ寄せる

### 6.2 `WhiteBrowserSkinDefinition`

将来的に次のような性質を追加できる形にする余地がある。

- `UsesCustomDisplayProfile`
- `SupportsManagedThumbnailGeneration`
- `PreferredDisplayProfile`

ただし Phase 1 では必須ではない。

### 6.3 `UpperTabDecodeProfile`

固定 static 値は卒業させる。
最終的には `ThumbnailDisplayProfile` から `DecodePixelHeight` を返す形へ置き換える。

### 6.4 `Views/Main/MainWindow.xaml`

固定サイズの直書きは次の順で剥がす。

1. Grid
2. Small
3. Big10
4. Big
5. List

理由は、Grid が最も単純で、Small/Big10 は既存サムネ再利用の影響を確認しやすいから。

### 6.5 `ThumbnailLayoutProfileResolver`

Phase 1〜3 では既存のまま維持する。
ここを早く触りすぎると、本線のテンポ改善とは別の大工事になる。

## 7. 受け入れ基準

### 7.1 外部 WB スキン

- `thum-width` / `thum-height` を変えると見た目が変わる
- `system.skin` と `profile.LastUpperTab` の互換を壊さない
- WebView2 未導入時は既存 fallback へ戻れる

### 7.2 built-in WPF

- 既定値では現状と同じ見た目
- Custom にすると表示サイズだけ変わる
- 一覧スクロールやページ移動の体感が許容範囲
- メモリ使用量が極端に増えない

### 7.3 生成

- Phase 1〜3 の間は、既存 thumb フォルダ構成を壊さない
- 同名画像優先表示 / 取り込み機能を壊さない
- ERROR マーカーや未作成判定の契約を広げない

## 8. リスク

1. WPF built-in タブは、画像サイズだけでなく周辺レイアウトも固定値依存が強い
2. decode サイズを上げると、一覧テンポとメモリに効く
3. custom 生成まで踏み込むと、thumb root 規約が一気に広がる
4. 外部 WB スキンと built-in WPF を同一責務で扱うと、両方が中途半端になる

## 9. 推奨する実装順

1. 外部 WB スキンで `thum-*` を丸めず表示へ使う
2. built-in 用 `ThumbnailDisplayProfile` を導入する
3. Grid だけ custom 表示対応する
4. Small / Big10 / Big / List へ広げる
5. その後も不足があれば、custom 生成の必要性を再評価する

## 10. 今回の判断

この機能は、**まず表示自由化から入るべき**である。
WhiteBrowser の思想にも合い、既存サムネ資産も守りやすい。

逆に、最初から生成自由化へ入る案はやりすぎである。
そこは表示自由化が十分に効くと確認できた後でよい。

## 11. 関連コード

- `WhiteBrowserSkin/WhiteBrowserSkinCatalogService.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinConfig.cs`
- `WhiteBrowserSkin/WhiteBrowserSkinDefinition.cs`
- `Views/Main/MainWindow.WebViewSkin.cs`
- `Views/Main/MainWindow.xaml`
- `UpperTabs/Common/UpperTabDecodeProfile.cs`
- `Thumbnail/MainWindow.ThumbnailPaths.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailLayoutProfile.cs`
