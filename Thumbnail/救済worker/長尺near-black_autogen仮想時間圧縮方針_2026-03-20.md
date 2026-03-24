# 長尺near-black autogen仮想時間圧縮方針 2026-03-20

最終更新日: 2026-03-20

## 1. 目的

- 長尺動画で、全体の大半は黒いが一部だけ明るい場面がある個体を救う
- `autogen` の候補時刻が後方へ広がりすぎる問題を、救済worker 限定で緩和する
- 通常本線や既定の rescue route を重くしない

## 2. 前提

- 対象は `near-black-or-old-frame` 系である
- `CODEC NG` や `NoVideoStream` ではない
- `ffmpeg1pass` / `autogen` の通常試行や黒再取得でも、十分な明所が拾えない
- 実動画時間そのものは正しい

ここで問題なのは動画長ではなく、`autogen` の候補時刻分布である。

## 3. 基本方針

- 実時間は書き換えない
- DB も metadata も変更しない
- `autogen` へ渡す候補時刻計算だけ、**仮想動画時間** を使う
- この処理は **救済worker 限定**
- 通常本線には入れない

## 4. 適用条件

次を全部満たした時だけ発火候補にする。

- route が `route-near-black-or-old-frame`
- 実動画時間が `2時間以上`
- 通常の `autogen`
- `black_retry`
- `ffmpeg1pass`
  を通しても near-black reject が続く

## 5. 仮想時間圧縮の考え方

`autogen` の候補計算時だけ、動画長を短く見せる。

例:

- 実 duration = `10800 sec`
- 仮想 duration = `2700 sec` (`1/4`)

この時、`autogen` の割合ベース候補は前方へ圧縮される。

## 6. 固定ルール

- 初回の圧縮率は固定 `1/4` にしない
- 次の順で **段階圧縮** する
  - `1/2`
  - `1/3`
  - `1/4`
- 必要になった時だけ次段候補として
  - `1/6`
  を検討する

理由:

- `1/4` 固定だと「前半 1/4 だけ明るい」個体には効く
- しかし「前半 1/2 にはある」「前半 1/3 にだけある」個体を取りこぼす
- なので、小さい圧縮から順に試す方が自然で安全

## 7. 実行順

1. 通常 `autogen`
2. near-black reject
3. `autogen(virtual=1/2)`
4. まだ near-black なら `autogen(virtual=1/3)`
5. まだ near-black なら `autogen(virtual=1/4)`
6. それでもダメなら既存の次 engine へ進む

## 8. 実装位置

- `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree\src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`

実装済み構造:

- `route-near-black-or-old-frame` かつ `autogen` かつ `2時間以上` の時だけ発火する
- 通常 `black_retry` が尽きた後に
  - `1/2`
  - `1/3`
  - `1/4`
  の仮想時間で `ThumbInfo` override を組み直し、`autogen` を再実行する
- 実動画時間、DB、通常本線は変更しない

## 9. 通常ルートへ入れない理由

- 長尺個体だけを狙う特殊処理である
- 通常動画の体感テンポに価値がない
- route 条件と観測ログが揃わないと、ただ重いだけの再試行になる

## 10. 観測項目

- runtime log
  - `autogen_virtual_duration`
- trace
  - `action=autogen_virtual_duration`
  - `result=start / rejected / failed / success`
- 記録するもの
  - 実 duration
  - 仮想 duration
  - 圧縮率
  - capture 秒
  - near-black 判定

## 11. 成功条件

- near-black reject だった長尺個体で、通常 `autogen` より明るいフレームを返せる
- 既存の `black_retry` より前寄りの候補で改善が見える
- 通常 route の順番や時間予算を壊さない

## 12. 非目標

- `CODEC NG` 個体を救うこと
- `NoVideoStream` 個体を救うこと
- 全長を高密度で総当たりすること
- 手動専用の長尺全域走査を通常 route へ混ぜること

## 13. 手動ロジックとの関係

- `Invoke-ManualBrightFrameLoop_2026-03-19.ps1` は別用途で維持する
- そちらは「長尺全体を人間向けに探索する」manual-only
- 本方針は「救済worker の中で軽く前方圧縮する」route-limited

両者は似ているが責務が違うので混ぜない。

## 14. 実装状況

- 2026-03-20 時点で初版実装済み
- `RescueWorkerApplicationTests` に
  - 発火条件
  - `1/2 -> 1/3 -> 1/4`
  の plan 生成
  を追加済み
