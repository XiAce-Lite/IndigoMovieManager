# 救済worker 資料案内

このフォルダは、救済worker と救済exe 改善に閉じる資料と補助スクリプトをまとめる。

## 入口

- `攻略台帳_難読wb全動画制覇_2026-03-15.md`
  - `C:\WhiteBrowser\難読.wb` を 1 動画ずつ制覇するための主台帳。
- `参考文献_完全勝利doc_Gridタブ成功パターン整理_2026-03-15.md`
  - `IndigoMovieManager_fork` 側で確認された Grid タブ成功パターンの参照コピー。
  - `out1.avi`、`shiroka8.mp4`、`古い.wmv` などの既知勝ち筋を引く入口。
- `伝達書_救済worker_Debug実行切り分け_2026-03-15.md`
  - Debug 実測で確認した現状と未解消項目の共有用。
- `中期計画_救済exe段階改善_2026-03-15.md`
  - `p1` から `p6` までの反復改善計画。
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

## 運用メモ

- 救済worker 専用の資料は、原則ここへ寄せる。
- `難読.wb` 攻略時は、まず `攻略台帳_難読wb全動画制覇_2026-03-15.md` を更新してから個別資料へ降りる。
- 既知の勝ち筋を引きたい時は、まず `参考文献_完全勝利doc_Gridタブ成功パターン整理_2026-03-15.md` を確認する。
- 実動画確認全体の手順や本exe 側の資料は親の `Thumbnail` 直下や `Docs/` に残す。
