# Everything フォールバック条件表（2026-03-03）

## 1. 目的
- reasonコードごとに、ホスト側の期待動作を一意に定義する。

## 2. 条件表

| 区分 | reason（一致条件） | 期待動作 | strategy |
|---|---|---|---|
| 設定 | `setting_disabled` | Everything経路を使わない | `filesystem` |
| 可用性 | `auto_not_available` | AUTO時のみ通常経路継続 | `filesystem` |
| 可用性 | `everything_not_available` | ON時でも通常経路へ切替 | `filesystem` |
| 可用性 | `availability_error:*` | 例外を飲み込み通常経路へ切替 | `filesystem` |
| 打ち切り | `everything_result_truncated:*` | 不完全結果を採用せず通常経路へ切替 | `filesystem` |
| 動画検索例外 | `everything_query_error:*` | 通常経路へ切替 | `filesystem` |
| サムネ検索例外 | `everything_thumb_query_error:*` | サムネBody収集を通常走査へ切替 | `filesystem` |
| 経路判定 | `path_not_eligible:*` | 対象外として通常経路へ切替 | `filesystem` |
| 動画検索成功 | `ok:*` | Everything結果を採用 | `everything` |
| サムネ検索成功 | `ok` | Everything結果を採用 | `everything` |
| 不明 | その他 | 不明理由として通常経路へ切替 | `filesystem` |

## 3. 補足ルール
- `path_not_eligible:ok` は運用上は発生想定外だが、受信時は通常経路扱いとする。
- `ok:*` は動画検索成功専用、`ok` はサムネ検索成功専用として扱う。
- strategy決定はFacade責務、通知文言生成はホスト責務とする。

## 4. 検証観点
- `everything_result_truncated:*` のとき `MoviePaths` が採用されないこと。
- `availability_error:*` / `everything_query_error:*` で例外伝播せず処理継続すること。
- `ok:*` / `ok` のときのみ `strategy=everything` になること。
