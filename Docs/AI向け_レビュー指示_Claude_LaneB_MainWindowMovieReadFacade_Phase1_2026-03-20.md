# AI向け レビュー指示 Claude LaneB MainWindowMovieReadFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- `Lane B: MainWindow movie read facade` の Phase1 差分

## 2. 見る観点

- `MainWindow` の read-only DB 入口が facade へ寄っているか
- facade に UI 状態反映や画面ロジックが逆流していないか
- 今回の対象外である `history` / `watch` / `bookmark` / write へ広がっていないか
- `StartupDbPageReader` と一覧再読込の責務が二重化していないか
- テスト不足が無いか

## 3. finding の出し方

- finding first
- 重大度順
- file:line を付ける
- 主眼は
  - バグ
  - 責務逆流
  - 完成形と逆向きの依存
  - テスト不足

## 4. 受け入れの目安

- 4 口の read が 1 本の read facade に見える
- `MainWindow` は UI 状態反映へ寄っている
- write が混ざっていない
- 次に `Data DLL` 実 project を作る時の移植単位が明確
