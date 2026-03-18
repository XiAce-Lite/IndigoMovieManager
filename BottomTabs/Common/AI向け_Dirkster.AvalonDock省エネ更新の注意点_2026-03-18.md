# AI向け Dirkster.AvalonDock 省エネ更新の注意点 (2026-03-18)

## 目的

- `Dirkster.AvalonDock` を使う下部タブで、非アクティブ時の無駄更新を止めつつ、再表示時に破綻しない実装方針を共有する。
- `IsVisible` / `IsSelected` / `IsActive` の意味差でハマる事故を減らす。
- `dirty` 管理、タイマー停止、イベント駆動更新の使い分けを固定する。

## 先に結論

- 重いポーリングは `IsSelected` または `IsActive` の時だけ動かす。
- 非アクティブ時は `dirty` を立て、再表示時に 1 回だけ反映する。
- ただし、実処理側から飛んでくる進捗イベントまで全部止めると、再表示後の見え方が古くなりやすい。
- `LayoutAnchorable` の `IsVisible` は「前面で見ている」とは限らない。重い処理の起動条件にはそのまま使わない。
- 初回レイアウト時に必要な枠が無いと、幅 0 のまま見えなくなることがある。待機枠は先に作る。

## AvalonDock で意識する状態

### `IsHidden`

- 本当に隠れている状態。
- ここで重い更新を回す意味は薄い。

### `IsVisible`

- レイアウト上は見えている扱いでも、ユーザーが今そのタブを前面で見ているとは限らない。
- 省エネ目的の起動条件に使うと、`見えているだけ` でタイマーやログ追従が動きやすい。

### `IsSelected`

- 同じ `LayoutAnchorablePane` 内で前面のタブかを見る。
- 下部タブの「今見ているか」判定ではまずこれを優先する。

### `IsActive`

- フォーカス移動を含む活性状態。
- `IsSelected` だけでは拾いきれないケースの補助に使う。

## 省エネ更新で分けるべき 3 種類

### 1. 重いポーリング更新

- 例: ログファイル追従、定期 DB 再読込、頻繁な UI スナップショット再構築。
- 方針:
  - `IsSelected || IsActive` の時だけタイマーを動かす。
  - 非アクティブ化したらタイマー停止。
  - その間に更新要求が来たら `dirty` だけ立てる。
  - 再アクティブ化時に 1 回だけ反映する。

### 2. 実処理から飛ぶイベント更新

- 例: キュー処理の進捗、worker 開始/終了、設定変更直後の軽い状態反映。
- 方針:
  - タイマーは止めてもよい。
  - ただし ViewModel 同期まで完全停止すると、再表示時に内容が古くなりやすい。
  - 「非アクティブ中も ViewModel だけは更新し、ポーリングだけ止める」構成は有効。

### 3. 初回レイアウトに効く初期状態

- 例: Thread カード枠、詳細ペインの器、初期 placeholder。
- 方針:
  - 初回表示に必要な枠は、最初の測定より前に持たせる。
  - 後から項目を増やすだけだと、AvalonDock 側で狭い幅のまま残ることがある。

## よくある失敗

### 失敗 1: `IsVisible` だけで重い処理を動かす

- タブを前面で見ていないのに更新が回る。
- 省エネ化のつもりが、実際は止まっていない。

### 失敗 2: 非アクティブ時に全部止めて、再表示時の復元が無い

- `dirty` を立てても、アクティブ化時に再構成する入口が無いと表示が古いままになる。
- `PropertyChanged` を監視していても、`dirty` 条件で弾きすぎると復元できない。

### 失敗 3: `LayoutAnchorable` 内の `UserControl` が期待どおりに bind されない

- `DataContext` 継承を前提にすると、host 構造次第で空 bind になることがある。
- 一覧やカードが出ない時は、`LayoutAnchorable` 配下の host に `DataContext="{Binding}"` が必要かを疑う。

### 失敗 4: 初回の幅計算時にアイテム数が 0

- 右側カードや補助ペインが、最初の measure で幅 0 になる。
- 後からアイテムを足しても、見た目が復活しにくいことがある。

### 失敗 5: 新しいタブを追加しても古い `layout.xml` がそれを持っていない

- `ContentId` が古いレイアウトに無いと、タブ自体が出ない。
- 新タブ追加時はレイアウト互換を必ず確認する。

## このリポジトリでの実装指針

### 共通 gate

- `BottomTabs/Common/BottomTabActivationGate.cs`
  - 共通の `IsVisibleOrSelected` 判定を持つ。
  - ただし、重い処理で `IsVisible` を含めるかは用途ごとに見直す。

### タブ専用 gate を持ってよい

- `BottomTabs/ThumbnailProgress/ThumbnailProgressTabVisibilityGate.cs`
  - `サムネ進捗` はポーリング停止を優先するため、`IsSelected || IsActive` に寄せている。

### dirty だけでは足りないケースがある

- `BottomTabs/Extension/MainWindow.BottomTab.Extension.cs`
  - 詳細タブは、アクティブ化時に現在選択から表示状態を再構成する必要がある。
  - `dirty` 条件だけで弾くと、前面に出したのに詳細が見えないことがある。

### タイマー停止とイベント反映は分ける

- `BottomTabs/ThumbnailProgress/MainWindow.BottomTab.ThumbnailProgress.cs`
  - 非アクティブ時に 500ms タイマーは止める。
  - ただし、worker 進捗のような実イベント反映まで止めるとカードが古くなる。

### 初期枠を持たせる

- `ViewModels/ThumbnailProgressViewState.cs`
  - 初回レイアウトで必要なカード枠は、後出しではなく先に作る方が安定する。

## 実装チェックリスト

- `LayoutAnchorable.PropertyChanged` を監視しているか
- 監視対象プロパティを `IsSelected / IsActive / IsVisible / IsHidden` で必要最小限にしているか
- 重いタイマーは非アクティブ時に止まるか
- 非アクティブ中の更新要求は `dirty` または軽い ViewModel 同期へ逃がしているか
- 再表示時に 1 回だけ復元する入口があるか
- 初回 measure 前に必要な枠を用意しているか
- `DataContext` が本当に届いているか
- 新しい `ContentId` を追加した時、古い `layout.xml` でも復帰できるか

## 迷った時の優先順位

1. ユーザーが今見ているタブのテンポを守る
2. 非アクティブ時の重い更新を止める
3. 再表示時に 1 回で正しい状態へ戻す
4. それでも足りない時だけ、イベント駆動の軽い同期を残す
