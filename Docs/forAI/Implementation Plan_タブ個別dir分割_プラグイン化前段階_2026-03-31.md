# Implementation Plan: タブ個別dir分割 プラグイン化前段階 (2026-03-31)

## 1. 目的

- 上部タブ、下部タブを、いきなりプラグイン化せずにまず個別 dir 単位で追える構成へ寄せる。
- 分割開発、レビュー、差分確認、担当分担をやりやすくする。
- `workthree` 本線の最優先であるユーザー体感テンポを落とさず、段階的に責務分離を進める。

## 2. 今回の結論

- 先にやるべきは「プラグイン化」ではなく「個別 dir 化」である。
- まず `MainWindow` 直下のタブ固有責務を、`UpperTabs/<TabName>` / `BottomTabs/<TabName>` へ寄せる。
- `MainWindow` は shell と host と接続だけを持つ形へ寄せる。
- interface は最初から重く入れず、まず置き場と依存線を分ける。

## 3. 背景

- 下部タブは、すでに `BottomTabs/<TabName>` へ寄せる流れが始まっている。
- 上部タブは、`Rescue` と `DuplicateVideos` は分割が進んでいるが、通常タブ (`Small / Big / Grid / List / Big10`) はまだ `MainWindow.xaml` 直結が濃い。
- この状態でプラグイン化へ直接進むと、host 契約の切り出しと既存依存の整理が同時に発生し、差分が大きくなりやすい。

## 4. 非目標

- 今回の段階では、別 DLL の動的ロードまでは行わない。
- 今回の段階では、`ITabPlugin` のような大きい共通 interface は導入しない。
- 今回の段階では、上部通常タブの XAML を一気に全面分離しない。
- 今回の段階では、重い MVVM への全面移行は行わない。

## 5. 成功条件

- タブ固有コードの置き場が、見て分かる形で `UpperTabs/<TabName>` / `BottomTabs/<TabName>` に揃う。
- `MainWindow` 側に残る責務が、host / shell / ライフサイクル / 全体接続に縮む。
- 1 タブ単位で差分を追える。
- 1 タブ単位で担当を分けても衝突しにくい。
- 体感テンポを悪化させない。

## 6. 基本方針

### 6.1 `MainWindow` に残すもの

- 上下タブ host の配置
- `DockingManager` / `TabControl` / レイアウト全体
- アプリ全体ライフサイクル
- 全体設定の反映
- タブ共通の最上位 ON/OFF
- タブ間をまたぐ最小限の接続

### 6.2 各タブ dir へ寄せるもの

- タブ固有の View
- タブ固有の表示更新条件
- タブ固有のイベント橋渡し
- タブ固有の読み込み処理
- タブ固有の選択追従
- タブ固有の軽い状態管理

### 6.3 共通 dir に置くもの

- タブ共通の活性判定
- タブ共通の index / identity
- タブ共通の viewport / page scroll 補助
- タブ共通の最小 helper

## 7. 推奨ディレクトリ構成

```text
UpperTabs/
  Common/
  Small/
  Big/
  Grid/
  List/
  Big10/
  Rescue/
  DuplicateVideos/

BottomTabs/
  Common/
  Extension/
  Bookmark/
  SavedSearch/
  ThumbnailProgress/
  ThumbnailError/
  DebugTab/
```

## 8. 実施順

### Phase 1: 下部タブの分割完成を先に進める

対象:
- `SavedSearch`
- `Bookmark`
- `Debug`
- `ThumbnailError`
- `ThumbnailProgress`
- `Extension`

目的:
- すでに始まっている `BottomTabs/<TabName>` 方針を完成側へ寄せる。
- 下部タブを「個別 dir 化の標準形」にする。

実施内容:
- `MainWindow.xaml` 側は `LayoutAnchorable` の host だけを維持する。
- タブ固有処理は各 `BottomTabs/<TabName>` の partial と View へ寄せる。
- `MainWindow` からは「初期接続」「表示切替」「全体設定反映」だけを呼ぶ。

完了条件:
- 下部タブは、どのタブを触るかがフォルダ名だけで分かる。
- タブごとの差分が `MainWindow.xaml.cs` へ戻りにくくなる。

### Phase 2: 上部の特殊タブを標準形に揃える

対象:
- `UpperTabs/Rescue`
- `UpperTabs/DuplicateVideos`

目的:
- すでに分かれ始めている上部特殊タブを、今後の標準形として整える。
- 上部通常タブ分割の見本を先に作る。

実施内容:
- `MainWindow.xaml` 側は host だけを持つ。
- 一覧構築、イベント橋渡し、選択同期、表示更新を `UpperTabs/<TabName>` へ寄せる。
- `MainWindow` 直参照が濃い箇所は、薄い helper 経由へ寄せる。

完了条件:
- `Rescue` / `DuplicateVideos` が、上部タブ個別 dir 化の完成例として使える。

### Phase 3: 上部通常タブのコード責務を先に分ける

対象:
- `Small`
- `Big`
- `Grid`
- `List`
- `Big10`

目的:
- XAML 全面分離の前に、コード責務の置き場を先に分ける。
- 通常タブの開発担当を分けやすくする。

実施内容:
- まず partial を切る。
- `MainWindow.UpperTab.<TabName>.cs` を追加する。
- 選択、表示更新、スクロール補助、タブ固有 helper を対象 dir へ寄せる。
- XAML は最初は直置きのままでもよい。

完了条件:
- 各通常タブのコード責務が、フォルダごとに追える。
- `MainWindow.xaml.cs` から通常タブ固有処理が減る。

### Phase 4: 上部通常タブの host 化を段階導入する

対象:
- `Grid`
- `List`
- `Small`
- `Big`
- `Big10`

順番の理由:
- `Grid` は基準形にしやすい。
- `List` は別系統の `DataGrid` として早めに境界を固定したい。
- `Small / Big / Big10` は visual tree が重く、後ろに置く方が安全。

実施内容:
- 必要なタブから `UserControl` 化する。
- `MainWindow.xaml` 側は `TabItem` の中身ではなく host を持つ。
- visible-first の共通基盤は `UpperTabs/Common` へ残す。

完了条件:
- 上部通常タブも、host + 個別 dir の形に揃う。

## 9. タブ別の優先順位

### 9.1 先に進めやすいもの

- `BottomTabs/SavedSearch`
- `BottomTabs/Bookmark`
- `BottomTabs/DebugTab`
- `UpperTabs/Rescue`
- `UpperTabs/DuplicateVideos`

理由:
- 既に専用 View や専用 dir がある。
- `MainWindow` との結合が相対的に薄い。
- 差分を小さく刻みやすい。

### 9.2 中盤で進めるもの

- `BottomTabs/ThumbnailError`
- `BottomTabs/ThumbnailProgress`

理由:
- 既に dir 分割は進んでいる。
- ただし UI 更新条件やポーリング、救済導線との結合が残る。

### 9.3 最後に進めるもの

- `BottomTabs/Extension`
- `UpperTabs/Grid`
- `UpperTabs/List`
- `UpperTabs/Small`
- `UpperTabs/Big`
- `UpperTabs/Big10`

理由:
- `Extension` は詳細表示、選択追従、詳細サムネ作成が濃い。
- 上部通常タブは `MainWindow.xaml` 直結と visible-first 基盤の影響が大きい。

## 10. 命名規則

- partial class: `MainWindow.BottomTab.<TabName>.cs`
- partial class: `MainWindow.UpperTab.<TabName>.cs`
- View: `<TabName>TabView.xaml`
- View code-behind: `<TabName>TabView.xaml.cs`
- 活性判定: `<TabName>TabVisibilityGate.cs`
- スクロール補助: `<TabName>TabScrollHelper.cs`

`Manager` や `Service` は広すぎる責務になりやすいため、まずは避ける。

## 11. 実装時の禁止線

- いきなりプラグイン契約まで作らない。
- 1 回の変更で複数タブをまとめて大移動しない。
- 上部通常タブの XAML を一気に全部抜かない。
- `MainWindow` から追い出した責務を、別の巨大共通クラスへ再集中させない。
- 体感テンポを悪化させる全件再描画や全件再バインドを増やさない。

## 12. 1 タブあたりの実装テンプレート

### Step 1

- 対象タブ dir を作る、または既存 dir を正式な置き場として確定する。

### Step 2

- タブ固有 partial を追加する。

### Step 3

- View とイベント橋渡しを対象 dir へ寄せる。

### Step 4

- `MainWindow` に残す呼び出し口を 1 から 3 個程度へ絞る。

### Step 5

- 非表示時の更新抑制や dirty 管理が必要なら、そのタブ dir 側へ寄せる。

### Step 6

- 関連ドキュメントを同時更新する。

## 13. レビュー観点

- この差分は、どのタブの責務をどこへ移したかが一目で分かるか。
- `MainWindow` 側の責務は本当に減っているか。
- 他タブ依存を増やしていないか。
- visible-first、選択同期、救済導線、Watcher 導線の既存挙動を壊していないか。
- 1 タブ単位で rollback しやすい差分になっているか。

## 14. 最初の着手候補

最初の 1 本は、次のどれかに限定する。

1. `BottomTabs/SavedSearch`
2. `BottomTabs/Bookmark`
3. `BottomTabs/DebugTab`

理由:
- 差分が小さい。
- 既存構造との衝突が少ない。
- 個別 dir 化の型を安全に固定できる。

## 15. この計画の次段

この計画が一巡した後に初めて、次を検討する。

- host 契約の共通化
- `ITabRuntimeContext` 相当の薄い受け口整理
- 上下タブの feature 単位ロード
- 別 DLL を含む本格的なプラグイン化

順序は固定する。

1. 個別 dir 化
2. host 契約の薄化
3. feature 単位化
4. 必要なら本物のプラグイン化

この順なら、分割開発のしやすさを先に得ながら、後戻りしにくい構造を作れる。
