# Route固定方針 救済worker 2026-03-16

最終更新日: 2026-03-16

## 1. 目的

- 救済worker の処理経路は、2026-03-16 時点で一度固定する
- 以後の個体攻略は、この文書を基準に
  - 既存 route のどれで勝ったか
  - どこで途中昇格したか
  - どこで例外処理が必要だったか
  を積み上げる
- route を変える時は、live 実測とこの文書の両方を更新する

## 2. 先に結論

- `fixed / unclassified`
  - placeholder 起点の弱い親 reason を受ける入口として維持する
- `route-long-no-frames`
  - `No frames decoded`
  - `thumbnail normal lane timeout`
  - `ffmpeg one-pass failed`
  の主戦場として固定する
- `route-ultra-short-no-frames`
  - 単画像・超短尺系の主戦場として固定する
- `route-corrupt-or-partial`
  - `frame decode failed at sec=...`
  - index 破損疑い
  - `repair_probe_negative` 後 fallback
  の受け皿として固定する
- `route-near-black-or-old-frame`
  - route 自体は残す
  - ただし 2026-03-16 時点では主戦場ではない

## 3. 固定する route 一覧

### 3.1 `fixed / unclassified`

- 入口条件:
  - 親 `FailureReason` が弱い
  - placeholder 起点
  - 初手で症状が切れない
- direct 順:
  1. `ffmpeg1pass`
  2. `ffmediatoolkit`
  3. `autogen`
  4. `opencv`
- repair:
  - 許可する
  - ただし fixed のまま repair へ入るより、途中で既存 route へ昇格する方を優先する
- live で固まった勝ち筋:
  - `_steph__094110-vid1.mp4`
    - `ffmpeg1pass.direct`
  - `【ライブ配信】神回scale_2x_prob-3.mp4`
    - `ffmpeg1pass 1敗 -> ffmediatoolkit.direct`
  - detail / tab placeholder の軽量個体
    - 多くが `ffmpeg1pass.direct`

### 3.2 `route-long-no-frames`

- 入口条件:
  - `No frames decoded`
  - `thumbnail normal lane timeout`
  - `engine attempt timeout`
  - `ffmpeg one-pass failed`
- direct 順:
  1. `ffmpeg1pass`
  2. `ffmediatoolkit`
- repair 順:
  1. `ffmpeg1pass`
  2. `ffmediatoolkit`
  3. `autogen`
  4. `opencv`
- live で固まった勝ち筋:
  - `35967.mp4`
    - `ffmpeg1pass.direct`
  - `みずがめ座 (2).mp4`
    - `ffmpeg1pass.direct`
  - `真空エラー2_ghq5_temp.mp4`
    - `ffmpeg1pass.direct`
  - stale placeholder 代表
    - `ffmpeg1pass timeout -> ffmediatoolkit.direct`
- 固定ルール:
  - 長尺系はまず `ffmpeg1pass`
  - 次に `ffmediatoolkit`
  - それでも破損臭が強ければ `route-corrupt-or-partial` へ上げる

### 3.3 `route-ultra-short-no-frames`

- 入口条件:
  - 超短尺 / 単画像寄り
  - `No frames decoded`
- direct 順:
  1. `autogen`
  2. `ffmpeg1pass`
  3. `ffmediatoolkit`
  4. `opencv`
- repair:
  - 既定では使わない
- live で固まった勝ち筋:
  - `画像1枚ありページ.mkv`
    - `autogen 1敗 -> ffmpeg1pass.direct`
  - `画像1枚あり顔.mkv`
    - `autogen 1敗 -> ffmpeg1pass.direct`
  - `mpcクラッシュ_NGカット.tmp.flv`
    - `autogen.direct`
- 固定ルール:
  - 超短尺は `autogen` を先頭に維持する
  - `autogen` が落ちたら `ffmpeg1pass`
  - この route では repair を常用しない

### 3.4 `route-corrupt-or-partial`

- 入口条件:
  - `frame decode failed at sec=...`
  - index 破損疑い
  - `repair_probe_negative` 後 fallback
- direct 順:
  1. `ffmpeg1pass`
- repair 順:
  1. `ffmpeg1pass`
  2. `ffmediatoolkit`
  3. `autogen`
  4. `opencv`
- live で固まった勝ち筋:
  - `インデックス破壊-093-2-4K.mp4`
    - `probe_negative_fallback -> autogen`
  - `「ラ・ラ・ランド」は少女漫画か！？ 1_2.mp4`
  - `「ラ・ラ・ランド」は少女漫画か！？ 2_2.mp4`
    - `probe_negative_fallback -> autogen`
  - `mpcクラッシュ_NGカット.tmp.flv` の `frame decode failed at sec=9`
    - `ffmpeg1pass.direct`
- 固定ルール:
  - 破損疑いでは `ffmpeg1pass` を先に当てる
  - `repair_probe_negative` でも、残り engine を回す fallback を許可する

### 3.5 `route-near-black-or-old-frame`

- 入口条件:
  - 画は取れるが代表サムネとして弱い
- direct 順:
  1. `autogen`
  2. `ffmpeg1pass`
  3. `ffmediatoolkit`
- 現状:
  - route 自体は残す
  - 2026-03-16 時点では、この route を主戦場にする必要はまだない
  - ただし service 側では near-black jpg を成功扱いにせず reject する
  - `autogen-header-frame-fallback` でも near-black な `sec=0` hit は採用せず次候補へ進める

## 4. 途中昇格の固定ルール

### 4.1 `fixed -> route-long-no-frames`

- 条件:
  - `ffmpeg one-pass failed`
  - timeout 系
  - `No frames decoded`
- 意味:
  - placeholder 起点でも、最初の失敗で long 系へ寄せる

### 4.2 `fixed -> route-corrupt-or-partial`

- 条件:
  - `frame decode failed at sec=...`
  - index 破損臭が強い
- 意味:
  - 文言が弱い親行でも、direct failure から corruption 側へ寄せる

### 4.3 `route-long-no-frames -> route-corrupt-or-partial`

- 条件:
  - `repair_probe_negative`
  - 途中で index 系 failure が見えている
- 意味:
  - long 系で repair が空振りでも、fallback で `autogen` / `opencv` へ繋ぐ

### 4.4 `route-ultra-short-no-frames -> route-corrupt-or-partial`

- 条件:
  - direct を使い切った
  - 途中で index 系 failure が見えている
- 意味:
  - `out1.avi` 型のように、超短尺でも途中で corruption 側へ上げる

## 5. engine の扱いを固定する

### 5.1 `ffmpeg1pass`

- 最優先 engine
- 今の live 実測では最も勝率が高い
- placeholder / long / corrupt の主力として固定する

### 5.2 `ffmediatoolkit`

- `ffmpeg1pass` の次
- fixed / long では 2 手目固定
- `神回scale` のように `ffmpeg1pass 1敗後` の直勝ちがある

### 5.3 `autogen`

- 本体では単発で終える
- 救済worker では
  - ultra-short 先頭
  - corrupt fallback
  の 2 か所が主戦場
- `header-fallback` は使うが、near-black な先頭フレームは reject して次候補へ進める

### 5.4 `opencv`

- 最後尾固定
- child process 隔離のまま維持する
- nominal timeout は長め、hard timeout は親 kill 前提

## 7. 近黒フレームの固定方針

- main 側でも worker 側でも、near-black jpg は成功扱いにしない
- rescue worker では near-black reject 後、同じ engine のまま追加 4 回まで再取得する
  - `10%`
  - `35%`
  - `65%`
  - `85%`
  を整数秒へ丸めた候補で回す
- `route-near-black-or-old-frame` でも、`autogen` が黒jpgを返したら次の `ffmpeg1pass` / `ffmediatoolkit` へ進める
- 成功後は同一動画の stale `#ERROR.jpg` を消し、UI が古いエラー画像を拾わないようにする
- 既に正常 jpg がある個体へは、precheck / 通常失敗側でも `#ERROR.jpg` を再生成しない
- 詳細は `黒フレーム再取得方針_2026-03-16.md` を正本にする
- startup / periodic sync でも、成功 jpg と同居する stale `#ERROR.jpg` は掃除する

## 6. placeholder の固定方針

- `tab-error-placeholder`
- `detail-selection-error-placeholder`

この 2 つは 2026-03-16 時点で、次の順で見る。

1. `fixed / unclassified` に置く
2. `ffmpeg1pass` を先に当てる
3. 失敗時に `ffmediatoolkit`
4. それでも駄目な時だけ既存 route へ昇格する

理由:
- live 実測では、多くの placeholder 個体が direct だけで閉じる
- 先に重い route を切るより、まず direct 勝ち筋を棚卸しした方が速い

## 7. 例外として覚えておく個体

- `古い.wmv`
  - forced repair が必要
- `out1.avi`
  - `ultra-short -> corrupt-or-partial -> forced repair` が必要
- この 2 本は route の例外ではなく、repair 出口の特殊個体として扱う

## 8. いま固定しないもの

- `near-black` の細分類
- 新 route の追加
- `autogen` のより深い候補探索
- `opencv` を前へ出す判断

これらは、今の route で取れない個体が増えてから見直す。

## 8.5 既存 `CODEC NG` 固定個体

- `E:\_サムネイル作成困難動画\映像なし_scale_2x_prob-3(1)_scale_2x_prob-3.mkv`
  は既存 `CODEC NG` 扱いで固定する
- 理由:
  - `container_probe` / `ffprobe` では `video_present`
  - ただし `ffplay` 実測では `unspecified pixel format` で映像表示できない
- つまりこれは `NoVideoStream` ではなく、`video_present だが実質デコード不能` の個体である
- この個体に対しては、現時点で新 route や強引な救済を足さず、既存 `CODEC NG` 表示を許容する

## 9. 運用ルール

- route を変える時は、必ず live 実測を 1 本添える
- `攻略台帳_難読wb全動画制覇_2026-03-15.md` には
  - どの route で入ったか
  - どこで昇格したか
  - 何で勝ったか
  を残す
- 本文書の route 表を変える時は、`README.md` も同時に更新する

## 10. 一言まとめ

- 2026-03-16 時点では、新 route 追加より既存 route の固定運用が勝つ
- 本線は軽く、救済worker はこの route 表を基準に個体攻略を積む
