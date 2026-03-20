# AI向け レビュー指示 Claude LaneB FacadeGuardIntegrationTests Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- `Lane B facade guard / integration tests` の Phase1 差分

## 2. 見る観点

- T4
  - `sortId = 28` と unknown sortId の既定動作が test で固定されたか
  - movie read facade 配線が source-based test などで退行防止されているか
- T5
  - invalid path / read failure / null / empty guard の test が足りているか
- T6
  - UI / watcher 側から `UpdateMovieSingleColumn(...)` 直叩きへ戻らないことを固定できているか
- source-based test が広過ぎず、今回対象の Lane B に留まっているか
- production code 変更があれば、それが最小か

## 3. finding の出し方

- finding first
- 重大度順
- file:line を付ける

## 4. 受け入れの目安

- Lane B Phase1 の退行防止として妥当な test が追加されている
- 変更範囲が guard / integration test 補強に留まっている
- 新たな逆流や過度な結合が入っていない
