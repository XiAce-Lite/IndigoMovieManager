# 比較メモ: UpperTabs/Common と BottomTabs/Common の役割対応 (2026-04-01)

## 目的

- `UpperTabs/Common` と `BottomTabs/Common` の責務差を、次に触る AI がすぐ掴めるようにする。
- 「どの変更を上側へ置くか / 下側へ置くか」の判断を早くする。
- `Common` が太りすぎた時の逃がし先を見失わないようにする。

## 先に結論

- `UpperTabs/Common` は、上側メインタブの `identity / viewport / selection flow` を持つ。
- `BottomTabs/Common` は、下部タブ共通というより「上側タブ切替に連動するシェル処理」と「下部タブ活性判定の入口」を持つ。
- つまり:
  - 上側一覧そのものの振る舞いは `UpperTabs/Common`
  - 下部タブ側から見た共通窓口は `BottomTabs/Common`

## 読み順

1. `UpperTabs/Common`
   - 今どの上側タブか、何が選ばれているか、viewport がどこかを掴む。
2. `BottomTabs/Common`
   - 上側タブ切替で下部タブ側に何が伝播するかを見る。
3. 対象タブの dir
   - 実際の描画や defer、presenter / controller の責務を見る。

## 役割対応表

| 観点 | UpperTabs/Common | BottomTabs/Common |
| --- | --- | --- |
| 主責務 | 上側タブそのものの共通挙動 | 下部タブ側から見た共通窓口 |
| 今どのタブか | 持つ | 原則持たない。必要時は上側 helper を呼ぶ |
| 固定IDと `TabItem` 解決 | 持つ | 持たない |
| skin 名との往復 | 持つ | 持たない |
| viewport / visible range | 持つ | 持たない |
| 上側タブの選択解決 | 持つ | 呼び出すだけ |
| 上側タブの選択反映 | 持つ | 呼び出すだけ |
| 上側タブ切替本体 | 持つ | イベント受け口だけ持つ |
| 下部タブ活性判定 | 持たない | `BottomTabActivationGate` で持つ |
| 下部タブ host との接続 | 持たない | 必要な最小窓口だけ持つ |

## 今の実装での見分け方

### `UpperTabs/Common` に置くもの

- `tabIndex` や `TabItem` を直接扱う共通処理
- 上側通常タブと特殊タブの振り分け
- 上側一覧の選択、既定選択、詳細ペイン同期
- viewport の計測、差分、snapshot、ログ、自動救済

### `BottomTabs/Common` に置くもの

- `Tabs_SelectionChangedAsync()` みたいな UI イベントの受け口
- `SelectFirstItem()` みたいな下部側から使う共通窓口
- 下部タブの `dirty` / 活性判定の最小 helper
- 上側タブ切替に連動する「下部側のシェル処理」

## 迷った時の判断基準

### `UpperTabs/Common` に寄せるべきケース

- 「上側タブが何者か」を知っていないと成立しない
- `Small / Big / Grid / List / Big10 / 救済 / 重複動画` の振り分けが要る
- viewport や選択状態そのものを扱う

### `BottomTabs/Common` に寄せるべきケース

- 下部タブ側の入口としてだけ必要
- 上側の細かい差は hidden にして、呼び出すだけでよい
- `BottomTabActivationGate` と同じ文脈で読む方が自然

## 追加実装の置き場所ガイド

- 新しい上側タブ共通 helper:
  - まず `UpperTabs/Common`
- 下部タブから上側選択を引く薄い入口:
  - まず `BottomTabs/Common`
- 上側タブ切替で下部タブに副作用が走る受け口:
  - `BottomTabs/Common`
- 上側タブの本当の処理本体:
  - `UpperTabs/Common`

## 運用メモ

- `UpperTabs/Common` は `TryResolve... / Dispatch... / Finalize...` の読み味を揃える。
- `BottomTabs/Common` は「受け口を薄く、実処理は上側 or 各タブ dir へ逃がす」を守る。
- どちらにも置けそうなら、`tabIndex` と `TabItem` を直に知る必要があるかで決める。
