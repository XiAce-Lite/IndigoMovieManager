# UpperTabs ドキュメント案内

このフォルダは、上側タブ (`Small / Big / Grid / List / Big10 / 救済 / 重複動画`) の設計と実装資料の置き場です。

## 入口

- [設計メモ_上側タブ共通描画再検討_2026-03-15.md](設計メモ_上側タブ共通描画再検討_2026-03-15.md)
  - 上側タブの高速化を前提に、共通描画クラスではなく共通描画基盤を採る判断を整理した設計メモです。
- [Implementation Plan_上側タブvisible-first高速化_2026-03-15.md](Implementation Plan_上側タブvisible-first高速化_2026-03-15.md)
  - 上側タブの visible-first 高速化を、decode 最適化から queue priority まで段階導入する実装計画です。
- [Review_上側タブvisible-first高速化_2026-03-15.md](Review_上側タブvisible-first高速化_2026-03-15.md)
  - 上側タブの visible-first 高速化計画に対するレビューと、着手前に固めるべき論点です。
- [DuplicateVideos/Implementation Plan_重複動画タブ_2026-03-20.md](DuplicateVideos/Implementation Plan_重複動画タブ_2026-03-20.md)
  - 上側に `重複動画` タブを新設し、同一ハッシュ検出を手動分析タブとして導入する計画です。

## 方針

- 上側タブ専用の文書は、このフォルダへ寄せる。
- コードを追加したら、この `README.md` も同時に更新する。
- `MainWindow.xaml` 直下に置かれていた設計メモは、今後このフォルダを基準に辿る。
- タブ専用の実装計画は、対象別サブフォルダを切って置く。

## 実装メモ

- 上側タブの共通基盤コードは `UpperTabs/Common` に置く。
- `UpperTabs/Rescue` は、上側 `救済` タブ専用の UI と軽量一覧モデルを置く。
- `UpperTabs/DuplicateVideos` は、上側 `重複動画` タブの UI、検出、表示モデルを置く。2026-03-20 時点で左右2ペインの初期版を実装済み。
- 2026-03-15 時点で、Phase 1 の decode 最適化、Phase 2 の viewport 追跡、Phase 3 の visible-first lease 優先制御まで着手済み。
