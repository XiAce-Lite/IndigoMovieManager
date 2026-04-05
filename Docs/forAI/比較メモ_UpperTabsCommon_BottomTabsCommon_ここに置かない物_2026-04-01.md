# 比較メモ: UpperTabs/Common と BottomTabs/Common に置かない物 (2026-04-01)

## 目的

- `Common` が便利だからといって、責務の重い処理まで吸い込まないための禁止線を決める。
- 次に触る AI が「これは `Common` へ入れない」を早く判断できるようにする。

## 先に結論

- `Common` には「共通の入口」と「薄い振り分け」は置いてよい。
- `Common` には「そのタブ固有の本体処理」「重い描画ロジック」「個別タブ専用の状態保持」は置かない。

## `UpperTabs/Common` に置かない物

### 1. 個別タブ専用の描画本体

- `Small` だけの item テンプレート都合
- `Big` だけのクリック同期詳細
- `Rescue` だけの履歴組み立て
- `DuplicateVideos` だけのグループ詳細生成

これらは各 `UpperTabs/<TabName>` に置く。

### 2. 個別タブ専用の重いデータ構築

- `FailureDb` を直接叩いて一覧モデルを組み立てる処理
- 重複動画の検出計算
- 個別タブだけが必要とする変換・整形

`Common` には「呼び出す入口」だけを置き、本体は各 dir 側へ置く。

### 3. UI 部品に強く結び付いた処理

- 特定 `DataGrid` の列定義前提
- 特定 `ListViewItem` の visual tree 依存
- 特定 `UserControl` の内部名に依存する処理

共通化したくなっても、その UI に閉じたままなら各タブ側に残す。

## `BottomTabs/Common` に置かない物

### 1. 下部タブ個別の defer / dirty / timer 本体

- `Bookmark` の再読込条件
- `ThumbnailProgress` の tick 本体
- `DebugTab` のログ末尾更新
- `Extension` の詳細再構成

これらは presenter / controller / partial を各タブ dir に置く。

### 2. 個別タブ host の出し入れ詳細

- `ThumbnailError` の host attach / detach
- layout 復元時の個別掃除
- 特定タブだけの `ContentId` 調整

共通窓口が必要でも、実際の host 制御本体はタブ側へ置く。

### 3. 上側タブの本当の処理本体

- 上側タブの選択解決本体
- 上側タブの viewport 本体
- 上側タブの skin 互換解決本体

`BottomTabs/Common` は呼び出し元であって、実装本体は `UpperTabs/Common` に置く。

## 迷った時の判断基準

### `Common` に置いてよい

- 複数の呼び出し元から使う
- 振り分けだけで成立する
- タブ固有 UI の内情を知らなくてよい
- `TryResolve... / Dispatch... / Finalize...` の薄い入口にできる

### `Common` に置かない

- 1タブだけしか使わない
- 特定 XAML 名や visual tree を直接知る
- そのタブ専用の状態を持つ
- 処理量が重く、呼び出し元より本体の方が大きい

## 運用メモ

- 迷ったら先に各タブ dir へ置き、あとで本当に共通化が必要かを見る。
- `Common` は「再利用候補の墓場」にしない。
- 置き場所判断に迷ったら、`比較メモ_UpperTabsCommon_BottomTabsCommon_役割対応_2026-04-01.md` を先に見る。
