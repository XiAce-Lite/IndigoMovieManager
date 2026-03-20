# 救済worker 資料案内

このフォルダは、救済worker と救済exe 改善に閉じる資料と補助スクリプトをまとめる。

## 入口

- `攻略台帳_難読wb全動画制覇_2026-03-15.md`
  - `C:\WhiteBrowser\難読.wb` を 1 動画ずつ制覇するための主台帳。
- `Route固定方針_救済worker_2026-03-16.md`
  - 2026-03-16 時点で一旦固定した route 表。
  - `fixed / long / ultra-short / corrupt` の処理順と途中昇格ルールの基準。
- `黒フレーム再取得方針_2026-03-16.md`
  - near-black reject 後に、同じ engine を別時刻で撮り直す初版ポリシー。
- `長尺near-black_autogen仮想時間圧縮方針_2026-03-20.md`
  - 2時間以上の near-black 個体だけを対象に、救済worker 限定で `autogen` 候補時刻を前寄りへ圧縮する方針。
  - 初版は `1/2 -> 1/3 -> 1/4` の段階圧縮まで実装済み。
- `引き継ぎメモ_黒フレーム再取得と難読wb残件_2026-03-16.md`
  - 2026-03-16 時点の実装内容、残件、次の live 観測ポイント。
- `参考文献_完全勝利doc_Gridタブ成功パターン整理_2026-03-15.md`
  - `IndigoMovieManager_fork` 側で確認された Grid タブ成功パターンの参照コピー。
  - `out1.avi`、`shiroka8.mp4`、`古い.wmv` などの既知勝ち筋を引く入口。
- `伝達書_救済worker_Debug実行切り分け_2026-03-15.md`
  - Debug 実測で確認した現状と未解消項目の共有用。
- `中期計画_救済exe段階改善_2026-03-15.md`
  - `p1` から `p6` までの反復改善計画。
- `調査メモ_超巨大AV1_sango72GB_2026-03-20.md`
  - `sango72GB.mkv` のような超巨大 AV1 個体を、near-black ではなく seek 重さとして切るための調査メモ。
- `超巨大AV1専用seek戦略_2026-03-20.md`
  - `codec=av1 / 4K以上 / 20GB以上 / ffmpeg1pass 60秒超失敗` を対象にした manual-first の seek 戦略。
  - 2026-03-20 実験では、この考え方を rescue worker の `BigMovie` 最終救済へ仮組み込みしている。
- `設計メモ_救済exe処理順とFailureDb書込アルゴ再考_2026-03-15.md`
  - route 設計、FailureDb 書込方針、症状分岐の考え方。
- `未解決束レポート_p6_2026-03-15.md`
  - `p6` で見えた未解決束の固定メモ。

## 補助スクリプト

- `救済worker失敗束サマリ_2026-03-15.ps1`
  - `attempt_failed` を `RouteId / SymptomClass / Engine / FailureKind / FailureReason` 単位で束ねる。
- `救済worker未解決束サマリ_2026-03-15.ps1`
  - recent `gave_up` と長時間 `processing_rescue` を route 単位で洗う。
- `Invoke-RescueAttemptChildLive_2026-03-15.ps1`
  - `--attempt-child` を直接叩き、isolated engine child を外側 timeout で kill できるかを実動画で確認する。
- `Invoke-ManualBrightFrameLoop_2026-03-19.ps1`
  - 手動専用の黒回避ループ。長尺全体を走査して、明るい候補フレームを数枚だけ拾う。
- `Invoke-ManualHugeAv1SeekProbe_2026-03-20.ps1`
  - 超巨大 AV1 の 1 枚抜きが timeout に負けるかどうかを、指定秒ごとに手動で切る。

## 手動専用ロジック

- `手動黒回避ループ_明所探索_2026-03-19.md`
  - 通常ルートへ入れず、別用途として持つ manual-only の明所探索方針。

## 運用メモ

- 救済worker 専用の資料は、原則ここへ寄せる。
- `難読.wb` 攻略時は、まず `攻略台帳_難読wb全動画制覇_2026-03-15.md` を更新してから個別資料へ降りる。
- 既知の勝ち筋を引きたい時は、まず `参考文献_完全勝利doc_Gridタブ成功パターン整理_2026-03-15.md` を確認する。
- 実動画確認全体の手順や本exe 側の資料は親の `Thumbnail` 直下や `Docs/` に残す。
