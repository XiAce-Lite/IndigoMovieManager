# UpperTabs ドキュメント案内

このフォルダは、上側タブ (`Small / Big / Grid / List / Big10 / サムネ失敗`) の設計と実装資料の置き場です。

## 入口

- [設計メモ_上側タブ共通描画再検討_2026-03-15.md](設計メモ_上側タブ共通描画再検討_2026-03-15.md)
  - 上側タブの高速化を前提に、共通描画クラスではなく共通描画基盤を採る判断を整理した設計メモです。
- [Implementation Plan_上側タブvisible-first高速化_2026-03-15.md](Implementation Plan_上側タブvisible-first高速化_2026-03-15.md)
  - 上側タブの visible-first 高速化を、decode 最適化から queue priority まで段階導入する実装計画です。
- [Review_上側タブvisible-first高速化_2026-03-15.md](Review_上側タブvisible-first高速化_2026-03-15.md)
  - 上側タブの visible-first 高速化計画に対するレビューと、着手前に固めるべき論点です。

## 方針

- 上側タブ専用の文書は、このフォルダへ寄せる。
- コードを追加したら、この `README.md` も同時に更新する。
- `MainWindow.xaml` 直下に置かれていた設計メモは、今後このフォルダを基準に辿る。
